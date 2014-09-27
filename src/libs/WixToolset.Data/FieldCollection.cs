//-------------------------------------------------------------------------------------------------
// <copyright file="ReadOnlyKeyedCollection.cs" company="Outercurve Foundation">
//   Copyright (c) 2004, Outercurve Foundation.
//   This software is released under Microsoft Reciprocal License (MS-RL).
//   The license and further copyright text can be found in the file
//   LICENSE.TXT at the root directory of the distribution.
// </copyright>
//-------------------------------------------------------------------------------------------------

namespace WixToolset.Data
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An indexed, read-only collection of <see cref="Field"/> objects.
    /// </summary>
    public sealed class FieldCollection : ReadOnlyKeyedCollection<string, Field>
    {
        /// <summary>
        /// Creates a new indexed, read-only collection of <see cref="Field"/> objects.
        /// </summary>
        /// <param name="fields">The <see cref="Field"/> objects for the collection.</param>
        public FieldCollection(IEnumerable<Field> fields) : base(fields, StringComparer.Ordinal)
        {
        }

        /// <summary>
        /// Gets the number of items in the collection.
        /// </summary>
        /// <remarks>
        /// Provided for backward compatibility.
        /// </remarks>
        public int Length
        {
            get { return this.Count; }
        }

        /// <summary>
        /// Gets the column name for the <see cref="Field"/>.
        /// </summary>
        /// <param name="item">The <see cref="Field"/> from which the key is retrieved.</param>
        /// <returns>The column name for the <see cref="Field"/>.</returns>
        protected override string GetKeyForItem(Field item)
        {
            if (null == item)
            {
                throw new ArgumentNullException("item");
            }

            return item.Column.Name;
        }
    }
}
