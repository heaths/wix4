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
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// A base class for an indexed, read-only collection of items.
    /// </summary>
    /// <typeparam name="TKey">The type of key for the item.</typeparam>
    /// <typeparam name="TItem">The type of item.</typeparam>
    public abstract class ReadOnlyKeyedCollection<TKey, TItem> : IList<TItem>
    {
        private IEqualityComparer<TKey> comparer;
        private IList<TItem> orderedValues;
        private IDictionary<TKey, TItem> indexedValues;

        /// <summary>
        /// Creates a new indexed, read-only collection of <paramref name="items"/>.
        /// </summary>
        /// <param name="items">The items to add to the collection.</param>
        /// <param name="comparer">The equality comparer to use for the key.</param>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
        public ReadOnlyKeyedCollection(IEnumerable<TItem> items, IEqualityComparer<TKey> comparer = null)
        {
            if (null == items)
            {
                throw new ArgumentNullException("items");
            }

            if (null == comparer)
            {
                this.comparer = EqualityComparer<TKey>.Default;
            }

            this.orderedValues = new List<TItem>();
            this.indexedValues = new Dictionary<TKey, TItem>(this.comparer);

            foreach (var item in items)
            {
                this.Add(item);
            }

            this.IsReadOnly = true;
        }

        /// <summary>
        /// Gets the index of the given <paramref name="item"/> in the collection.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>The index of the item in the collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
        public int IndexOf(TItem item)
        {
            if (null == item)
            {
                throw new ArgumentNullException("item");
            }

            return this.orderedValues.IndexOf(item);
        }

        /// <summary>
        /// Gets the item at the specified <paramref name="index"/>.
        /// </summary>
        /// <param name="index">The index of the item to get.</param>
        /// <returns>The item at the specified <paramref name="index"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the range of the collection.</exception>
        /// <exception cref="InvalidOperationException">Attempt to modify the read-only collection.</exception>
        public TItem this[int index]
        {
            get { return this.orderedValues[index]; }
            set { throw new InvalidOperationException(WixDataStrings.EXP_ReadOnlyCollection); }
        }

        /// <summary>
        /// Gets the item with the given <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key of thre item to get.</param>
        /// <returns>The item with the given <paramref name="key"/>.</returns>
        public TItem this[TKey key]
        {
            get { return this.indexedValues[key]; }
        }

        /// <summary>
        /// Deterines if the given <paramref name="item"/> exists in the collection.
        /// </summary>
        /// <param name="item">The item to check.</param>
        /// <returns>Whether the given <paramref name="item"/> exists in the collection.</returns>
        public bool Contains(TItem item)
        {
            return this.orderedValues.Contains(item);
        }

        /// <summary>
        /// Determines if an item with the given <paramref name="key"/> exists in the collection.
        /// </summary>
        /// <param name="key">The key of the item to check.</param>
        /// <returns>Whether an item with the given <paramref name="key"/> exists in the collection.</returns>
        public bool ContainsKey(TKey key)
        {
            return this.indexedValues.ContainsKey(key);
        }

        /// <summary>
        /// Copies the ordered collection of items to the specified <paramref name="array"/>.
        /// </summary>
        /// <param name="array">The array into which the items are copied.</param>
        /// <param name="arrayIndex">the index into the array where the copy begins.</param>
        public void CopyTo(TItem[] array, int arrayIndex)
        {
            this.orderedValues.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Gets the number of items in the collection.
        /// </summary>
        public int Count
        {
            get { return this.orderedValues.Count; }
        }

        /// <summary>
        /// Gets whether the collection is read-only.
        /// </summary>
        public bool IsReadOnly { get; private set; }

        /// <summary>
        /// Gets an enumerator over the ordered items in the collection.
        /// </summary>
        /// <returns>An enumerator over the ordered items in the collection.</returns>
        public IEnumerator<TItem> GetEnumerator()
        {
            return this.orderedValues.GetEnumerator();
        }

        protected abstract TKey GetKeyForItem(TItem item);

        private void Add(TItem item)
        {
            if (this.IsReadOnly)
            {
                throw new InvalidOperationException(WixDataStrings.EXP_ReadOnlyCollection);
            }

            var key = this.GetKeyForItem(item);

            this.orderedValues.Add(item);
            this.indexedValues.Add(key, item);
        }

        void IList<TItem>.Insert(int index, TItem item)
        {
            throw new InvalidOperationException(WixDataStrings.EXP_ReadOnlyCollection);
        }

        void IList<TItem>.RemoveAt(int index)
        {
            throw new InvalidOperationException(WixDataStrings.EXP_ReadOnlyCollection);
        }

        void ICollection<TItem>.Add(TItem item)
        {
            this.Add(item);
        }

        void ICollection<TItem>.Clear()
        {
            throw new InvalidOperationException(WixDataStrings.EXP_ReadOnlyCollection);
        }

        bool ICollection<TItem>.Remove(TItem item)
        {
            throw new InvalidOperationException(WixDataStrings.EXP_ReadOnlyCollection);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
