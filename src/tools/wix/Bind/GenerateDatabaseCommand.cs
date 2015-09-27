﻿//-------------------------------------------------------------------------------------------------
// <copyright file="GenerateDatabaseCommand.cs" company="Outercurve Foundation">
//   Copyright (c) 2004, Outercurve Foundation.
//   This software is released under Microsoft Reciprocal License (MS-RL).
//   The license and further copyright text can be found in the file
//   LICENSE.TXT at the root directory of the distribution.
// </copyright>
//-------------------------------------------------------------------------------------------------

namespace WixToolset.Bind
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using WixToolset.Data;
    using WixToolset.Extensibility;
    using WixToolset.Msi;
    using WixToolset.Msi.Interop;

    internal class GenerateDatabaseCommand : ICommand
    {
        // String to be used for setting Row values to NULL that are non-nullable.
        private static readonly string NullString = ((char)21).ToString();

        private List<string> nonEmptyProductCodes;
        private List<string> nonEmptyTransformNames;
        private List<string> emptyTransformNames;

        internal GenerateDatabaseCommand()
        {
            this.nonEmptyProductCodes = new List<string>();
            this.nonEmptyTransformNames = new List<string>();
            this.emptyTransformNames = new List<string>();
        }

        public int Codepage { private get; set; }

        public IEnumerable<IBinderExtension> Extensions { private get; set; }

        public IEnumerable<IBinderFileManager> FileManagers { private get; set; }

        /// <summary>
        /// Whether to keep columns added in a transform.
        /// </summary>
        public bool KeepAddedColumns { private get; set; }

        public Output Output { private get; set; }

        public string OutputPath { private get; set; }

        public TableDefinitionCollection TableDefinitions { private get; set; }

        public string TempFilesLocation { private get; set; }

        /// <summary>
        /// Whether to use a subdirectory based on the <paramref name="databaseFile"/> file name for intermediate files.
        /// </summary>
        public bool SuppressAddingValidationRows { private get; set; }

        public bool UseSubDirectory { private get; set; }

        public void Execute()
        {
            // Add the _Validation rows.
            if (!this.SuppressAddingValidationRows)
            {
                Table validationTable = this.Output.EnsureTable(this.TableDefinitions["_Validation"]);

                foreach (Table table in this.Output.Tables)
                {
                    if (!table.Definition.Unreal)
                    {
                        // Add the validation rows for this table.
                        table.Definition.AddValidationRows(validationTable);
                    }
                }
            }

            // Set the base directory.
            string baseDirectory = this.TempFilesLocation;

            if (this.UseSubDirectory)
            {
                string filename = Path.GetFileNameWithoutExtension(this.OutputPath);
                baseDirectory = Path.Combine(baseDirectory, filename);

                // make sure the directory exists
                Directory.CreateDirectory(baseDirectory);
            }

            try
            {
                OpenDatabase type = OpenDatabase.CreateDirect;

                // set special flag for patch files
                if (OutputType.Patch == this.Output.Type)
                {
                    type |= OpenDatabase.OpenPatchFile;
                }

                Console.WriteLine("Opening database at: {0}", this.OutputPath);

                using (Database db = new Database(this.OutputPath, type))
                {
                    // Localize the codepage if a value was specified directly.
                    if (-1 != this.Codepage)
                    {
                        this.Output.Codepage = this.Codepage;
                    }

                    // if we're not using the default codepage, import a new one into our
                    // database before we add any tables (or the tables would be added
                    // with the wrong codepage).
                    if (0 != this.Output.Codepage)
                    {
                        this.SetDatabaseCodepage(db, this.Output.Codepage);
                    }

                    // Insert substorages (like transforms inside a patch).
                    if (0 < this.Output.SubStorages.Count)
                    {
                        using (View storagesView = new View(db, "SELECT `Name`, `Data` FROM `_Storages`"))
                        {
                            foreach (SubStorage subStorage in this.Output.SubStorages)
                            {
                                string transformFile = Path.Combine(this.TempFilesLocation, String.Concat(subStorage.Name, ".mst"));

                                // Bind the transform.
                                this.BindTransform(subStorage.Data, transformFile);

                                if (Messaging.Instance.EncounteredError)
                                {
                                    continue;
                                }

                                // Add the storage.
                                using (Record record = new Record(2))
                                {
                                    record.SetString(1, subStorage.Name);
                                    record.SetString(2, transformFile);
                                    storagesView.Modify(ModifyView.Assign, record);

                                }
                            }
                        }
                    }

                    // Some empty transforms may have been excluded.
                    // We need to remove these from the final patch summary information.
                    if (OutputType.Patch == this.Output.Type)
                    {
                        Table patchSummaryInfo = this.Output.EnsureTable(this.TableDefinitions["_SummaryInformation"]);
                        for (int i = patchSummaryInfo.Rows.Count - 1; 0 <= i; --i)
                        {
                            Row row = patchSummaryInfo.Rows[i];
                            if ((int)SummaryInformation.Patch.ProductCodes == (int)row[0])
                            {
                                if (0 < this.nonEmptyProductCodes.Count)
                                {
                                    string[] productCodes = new string[this.nonEmptyProductCodes.Count];
                                    this.nonEmptyProductCodes.CopyTo(productCodes, 0);
                                    row[1] = String.Join(";", productCodes);
                                }
                                else
                                {
                                    row[1] = GenerateDatabaseCommand.NullString;
                                }
                            }
                            else if ((int)SummaryInformation.Patch.TransformNames == (int)row[0])
                            {
                                if (0 < this.nonEmptyTransformNames.Count)
                                {
                                    string[] transformNames = new string[this.nonEmptyTransformNames.Count];
                                    this.nonEmptyTransformNames.CopyTo(transformNames, 0);
                                    row[1] = String.Join(";", transformNames);
                                }
                                else
                                {
                                    row[1] = GenerateDatabaseCommand.NullString;
                                }
                            }
                        }
                    }

                    foreach (Table table in this.Output.Tables)
                    {
                        Table importTable = table;
                        bool hasBinaryColumn = false;

                        // Skip all unreal tables other than _Streams.
                        if (table.Definition.Unreal && "_Streams" != table.Name)
                        {
                            continue;
                        }

                        // Do not put the _Validation table in patches, it is not needed.
                        if (OutputType.Patch == this.Output.Type && "_Validation" == table.Name)
                        {
                            continue;
                        }

                        // The only way to import binary data is to copy it to a local subdirectory first.
                        // To avoid this extra copying and perf hit, import an empty table with the same
                        // definition and later import the binary data from source using records.
                        foreach (ColumnDefinition columnDefinition in table.Definition.Columns)
                        {
                            if (ColumnType.Object == columnDefinition.Type)
                            {
                                importTable = new Table(table.Section, table.Definition);
                                hasBinaryColumn = true;
                                break;
                            }
                        }

                        // Create the table via IDT import.
                        if ("_Streams" != importTable.Name)
                        {
                            try
                            {
                                db.ImportTable(this.Output.Codepage, importTable, baseDirectory, this.KeepAddedColumns);
                            }
                            catch (WixInvalidIdtException)
                            {
                                // If ValidateRows finds anything it doesn't like, it throws
                                importTable.ValidateRows();

                                // Otherwise we rethrow the InvalidIdt
                                throw;
                            }
                        }

                        // insert the rows via SQL query if this table contains object fields
                        if (hasBinaryColumn)
                        {
                            StringBuilder query = new StringBuilder("SELECT ");

                            // Build the query for the view.
                            bool firstColumn = true;
                            foreach (ColumnDefinition columnDefinition in table.Definition.Columns)
                            {
                                if (!firstColumn)
                                {
                                    query.Append(",");
                                }

                                query.AppendFormat(" `{0}`", columnDefinition.Name);
                                firstColumn = false;
                            }
                            query.AppendFormat(" FROM `{0}`", table.Name);

                            using (View tableView = db.OpenExecuteView(query.ToString()))
                            {
                                // Import each row containing a stream
                                foreach (Row row in table.Rows)
                                {
                                    using (Record record = new Record(table.Definition.Columns.Count))
                                    {
                                        StringBuilder streamName = new StringBuilder();
                                        bool needStream = false;

                                        // the _Streams table doesn't prepend the table name (or a period)
                                        if ("_Streams" != table.Name)
                                        {
                                            streamName.Append(table.Name);
                                        }

                                        for (int i = 0; i < table.Definition.Columns.Count; i++)
                                        {
                                            ColumnDefinition columnDefinition = table.Definition.Columns[i];

                                            switch (columnDefinition.Type)
                                            {
                                                case ColumnType.Localized:
                                                case ColumnType.Preserved:
                                                case ColumnType.String:
                                                    if (columnDefinition.PrimaryKey)
                                                    {
                                                        if (0 < streamName.Length)
                                                        {
                                                            streamName.Append(".");
                                                        }
                                                        streamName.Append((string)row[i]);
                                                    }

                                                    record.SetString(i + 1, (string)row[i]);
                                                    break;
                                                case ColumnType.Number:
                                                    record.SetInteger(i + 1, Convert.ToInt32(row[i], CultureInfo.InvariantCulture));
                                                    break;
                                                case ColumnType.Object:
                                                    if (null != row[i])
                                                    {
                                                        needStream = true;
                                                        try
                                                        {
                                                            record.SetStream(i + 1, (string)row[i]);
                                                        }
                                                        catch (Win32Exception e)
                                                        {
                                                            if (0xA1 == e.NativeErrorCode) // ERROR_BAD_PATHNAME
                                                            {
                                                                throw new WixException(WixErrors.FileNotFound(row.SourceLineNumbers, (string)row[i]));
                                                            }
                                                            else
                                                            {
                                                                throw new WixException(WixErrors.Win32Exception(e.NativeErrorCode, e.Message));
                                                            }
                                                        }
                                                    }
                                                    break;
                                            }
                                        }

                                        // stream names are created by concatenating the name of the table with the values
                                        // of the primary key (delimited by periods)
                                        // check for a stream name that is more than 62 characters long (the maximum allowed length)
                                        if (needStream && MsiInterop.MsiMaxStreamNameLength < streamName.Length)
                                        {
                                            Messaging.Instance.OnMessage(WixErrors.StreamNameTooLong(row.SourceLineNumbers, table.Name, streamName.ToString(), streamName.Length));
                                        }
                                        else // add the row to the database
                                        {
                                            tableView.Modify(ModifyView.Assign, record);
                                        }
                                    }
                                }
                            }

                            // Remove rows from the _Streams table for wixpdbs.
                            if ("_Streams" == table.Name)
                            {
                                table.Rows.Clear();
                            }
                        }
                    }

                    // We're good, commit the changes to the new database.
                    db.Commit();
                }
            }
            catch (IOException)
            {
                // TODO: this error message doesn't seem specific enough
                throw new WixFileNotFoundException(new SourceLineNumber(this.OutputPath), this.OutputPath);
            }
        }

        private void BindTransform(Output transform, string outputPath)
        {
            BindTransformCommand command = new BindTransformCommand();
            command.Extensions = this.Extensions;
            command.FileManagers = this.FileManagers;
            command.TempFilesLocation = this.TempFilesLocation;
            command.Transform = transform;
            command.OutputPath = outputPath;
            command.NonEmptyProductCodes = this.nonEmptyProductCodes;
            command.NonEmptyTransformNames = this.nonEmptyTransformNames;
            command.EmptyTransformNames = this.emptyTransformNames;
            command.Execute();
        }

        /// <summary>
        /// Sets the codepage of a database.
        /// </summary>
        /// <param name="db">Database to set codepage into.</param>
        /// <param name="output">Output with the codepage for the database.</param>
        private void SetDatabaseCodepage(Database db, int codepage)
        {
            // write out the _ForceCodepage IDT file
            string idtPath = Path.Combine(this.TempFilesLocation, "_ForceCodepage.idt");
            using (StreamWriter idtFile = new StreamWriter(idtPath, false, Encoding.ASCII))
            {
                idtFile.WriteLine(); // dummy column name record
                idtFile.WriteLine(); // dummy column definition record
                idtFile.Write(codepage);
                idtFile.WriteLine("\t_ForceCodepage");
            }

            // try to import the table into the MSI
            try
            {
                db.Import(idtPath);
            }
            catch (WixInvalidIdtException)
            {
                // the IDT should be valid, so an invalid code page was given
                throw new WixException(WixErrors.IllegalCodepage(codepage));
            }
        }
    }
}
