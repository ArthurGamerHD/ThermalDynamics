using System;
using System.Collections.Generic;

namespace RichHudFramework
{
	namespace UI
	{
		public interface IReadOnlyHudCollection<TElementContainer, TElement> : IReadOnlyList<TElementContainer>

			where TElementContainer : IHudNodeContainer<TElement>, new()
			where TElement : HudNodeBase
		{
			IReadOnlyList<TElementContainer> Collection { get; }


			TElementContainer Find(Func<TElementContainer, bool> predicate);


			int FindIndex(Func<TElementContainer, bool> predicate);
		}

		public interface IReadOnlyHudCollection<TElementContainer> : IReadOnlyHudCollection<TElementContainer, HudElementBase>

			where TElementContainer : IHudNodeContainer<HudElementBase>, new()
		{ }

		public interface IReadOnlyHudCollection : IReadOnlyHudCollection<HudElementContainer, HudElementBase>
		{ }

		public interface IHudCollection<TElementContainer, TElement> : IReadOnlyHudCollection<TElementContainer, TElement>

			where TElementContainer : IHudNodeContainer<TElement>, new()
			where TElement : HudNodeBase
		{

			void Add(TElement element);


			void Add(TElementContainer container);


			void AddRange(IReadOnlyList<TElementContainer> newContainers);


			void Insert(int index, TElementContainer container);


			void InsertRange(int index, IReadOnlyList<TElementContainer> newContainers);


			bool Remove(TElementContainer collectionElement);


			bool Remove(Func<TElementContainer, bool> predicate);


			bool RemoveAt(int index);


			void RemoveRange(int index, int count);


			void Clear();
		}

		public interface IHudCollection<TElementContainer> : IHudCollection<TElementContainer, HudElementBase>

			where TElementContainer : IHudNodeContainer<HudElementBase>, new()
		{ }

		public interface IHudCollection : IHudCollection<HudElementContainer, HudElementBase>
		{ }
	}
}