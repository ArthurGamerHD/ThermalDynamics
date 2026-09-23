using System;
using System.Collections;
using System.Collections.Generic;

namespace RichHudFramework
{
	namespace UI
	{
		public class HudCollection<TElementContainer, TElement> : HudElementBase, IHudCollection<TElementContainer, TElement>

			where TElementContainer : IHudNodeContainer<TElement>, new()
			where TElement : HudNodeBase
		{
			public IReadOnlyList<TElementContainer> Collection { get; }

			public HudCollection<TElementContainer, TElement> CollectionContainer => this;

			public TElementContainer this[int index]
			{
				get
				{
					if (hudCollectionList.Count == 0 || index < 0 || index >= hudCollectionList.Count)
						throw new Exception($"Collection index out of range. Index: {index} Count: {hudCollectionList.Count}");

					return hudCollectionList[index];
				}
			}

			public int Count => hudCollectionList.Count;

			public bool IsReadOnly { get; }

			protected readonly List<TElementContainer> hudCollectionList;


			public HudCollection(HudParentBase parent = null) : base(parent)
			{
				IsReadOnly = false;

				hudCollectionList = new List<TElementContainer>();
				Collection = hudCollectionList;
			}


			public IEnumerator<TElementContainer> GetEnumerator() => hudCollectionList.GetEnumerator();
			IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


			public virtual void Add(TElement element)
			{

				var container = new TElementContainer();
				container.SetElement(element);
				Add(container);
			}


			public virtual void Add(TElementContainer container)
			{
				if (container.Element.Registered)
					throw new Exception("HUD element is already registered to another parent.");

				if (!container.Element.Register(this))
					throw new Exception("Failed to register HUD element with this collection.");

				hudCollectionList.Add(container);
			}


			public virtual void AddRange(IReadOnlyList<TElementContainer> newContainers)
			{
				NodeUtils.RegisterNodes<TElementContainer, TElement>(this, newContainers);
				hudCollectionList.AddRange(newContainers);
			}


			public virtual void Insert(int index, TElementContainer container)
			{
				if (!container.Element.Register(this))
					throw new Exception("Failed to register HUD element with this collection.");

				hudCollectionList.Insert(index, container);
			}


			public virtual void InsertRange(int index, IReadOnlyList<TElementContainer> newContainers)
			{
				NodeUtils.RegisterNodes<TElementContainer, TElement>(this, newContainers);
				hudCollectionList.InsertRange(index, newContainers);
			}


			public virtual bool Remove(TElementContainer entry)
			{
				if (entry?.Element.Parent != this || hudCollectionList.Count == 0)
					return false;

				if (hudCollectionList.Remove(entry))
					return entry.Element.Unregister();

				return false;
			}


			public virtual bool Remove(Func<TElementContainer, bool> predicate)
			{
				int index = hudCollectionList.FindIndex(x => predicate(x));

				if (index == -1)
					return false;

				var element = hudCollectionList[index].Element;
				hudCollectionList.RemoveAt(index);
				return element.Unregister();
			}


			public virtual bool RemoveAt(int index)
			{
				if (index < 0 || index >= hudCollectionList.Count || hudCollectionList[index].Element.Parent != this)
					return false;

				var element = hudCollectionList[index].Element;
				hudCollectionList.RemoveAt(index);
				return element.Unregister();
			}


			public virtual void RemoveRange(int index, int count)
			{
				NodeUtils.UnregisterNodes<TElementContainer, TElement>(this, hudCollectionList, index, count);
				hudCollectionList.RemoveRange(index, count);
			}


			public virtual void Clear()
			{
				NodeUtils.UnregisterNodes<TElementContainer, TElement>(this, hudCollectionList, 0, hudCollectionList.Count);
				hudCollectionList.Clear();
			}


			public virtual TElementContainer Find(Func<TElementContainer, bool> predicate)
				=> hudCollectionList.Find(x => predicate(x));


			public virtual int FindIndex(Func<TElementContainer, bool> predicate)
				=> hudCollectionList.FindIndex(x => predicate(x));


			public virtual bool Contains(TElementContainer item) => hudCollectionList.Contains(item);


			public virtual void CopyTo(TElementContainer[] array, int arrayIndex)
				=> hudCollectionList.CopyTo(array, arrayIndex);


			public override bool RemoveChild(HudNodeBase child)
			{
				if (child.Parent == this)
				{
					bool success = child.Unregister();
					if (success)
						RemoveChild(child);
					return success;
				}
				else if (child.Parent == null && children.Remove(child))
				{
					childHandles.Remove(child.DataHandle);

					for (int n = 0; n < hudCollectionList.Count; n++)
					{
						if (hudCollectionList[n].Element == child)
						{
							hudCollectionList.RemoveAt(n);
							break;
						}
					}

					return true;
				}

				return false;
			}
		}

		public class HudCollection<TElementContainer> : HudCollection<TElementContainer, HudElementBase>

			where TElementContainer : IHudNodeContainer<HudElementBase>, new()
		{

			public HudCollection(HudParentBase parent = null) : base(parent) { }
		}

		public class HudCollection : HudCollection<HudNodeContainer, HudNodeBase>
		{

			public HudCollection(HudParentBase parent = null) : base(parent) { }
		}
	}
}