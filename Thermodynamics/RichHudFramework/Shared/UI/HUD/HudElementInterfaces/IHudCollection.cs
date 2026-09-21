using System;
using System.Collections.Generic;

namespace RichHudFramework
{
	namespace UI
	{
		public interface IReadOnlyHudCollection<TElementContainer, TElement> : IReadOnlyList<TElementContainer>
/// <summary>new operation.</summary>
			where TElementContainer : IHudNodeContainer<TElement>, new()
			where TElement : HudNodeBase
		{
			IReadOnlyList<TElementContainer> Collection { get; }

/// <summary>Find operation.</summary>
			TElementContainer Find(Func<TElementContainer, bool> predicate);

/// <summary>FindIndex operation.</summary>
			int FindIndex(Func<TElementContainer, bool> predicate);
		}

		public interface IReadOnlyHudCollection<TElementContainer> : IReadOnlyHudCollection<TElementContainer, HudElementBase>
/// <summary>new operation.</summary>
			where TElementContainer : IHudNodeContainer<HudElementBase>, new()
		{ }

		public interface IReadOnlyHudCollection : IReadOnlyHudCollection<HudElementContainer, HudElementBase>
		{ }

		public interface IHudCollection<TElementContainer, TElement> : IReadOnlyHudCollection<TElementContainer, TElement>
/// <summary>new operation.</summary>
			where TElementContainer : IHudNodeContainer<TElement>, new()
			where TElement : HudNodeBase
		{
/// <summary>Adds a .</summary>
			void Add(TElement element);

/// <summary>Adds a .</summary>
			void Add(TElementContainer container);

/// <summary>Adds a range.</summary>
			void AddRange(IReadOnlyList<TElementContainer> newContainers);

/// <summary>Insert operation.</summary>
			void Insert(int index, TElementContainer container);

/// <summary>InsertRange operation.</summary>
			void InsertRange(int index, IReadOnlyList<TElementContainer> newContainers);

/// <summary>Removes the .</summary>
			bool Remove(TElementContainer collectionElement);

/// <summary>Removes the .</summary>
			bool Remove(Func<TElementContainer, bool> predicate);

/// <summary>Removes the at.</summary>
			bool RemoveAt(int index);

/// <summary>Removes the range.</summary>
			void RemoveRange(int index, int count);

/// <summary>Clear operation.</summary>
			void Clear();
		}

		public interface IHudCollection<TElementContainer> : IHudCollection<TElementContainer, HudElementBase>
/// <summary>new operation.</summary>
			where TElementContainer : IHudNodeContainer<HudElementBase>, new()
		{ }

		public interface IHudCollection : IHudCollection<HudElementContainer, HudElementBase>
		{ }
	}
}