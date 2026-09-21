using System;
using System.Collections.Generic;

namespace RichHudFramework.UI
{
	public class TreeBox<TContainer, TElement> : TreeBoxBase<TContainer, TElement>
		where TElement : HudElementBase, IMinLabelElement
/// <summary>new operation.</summary>
		where TContainer : class, ISelectionBoxEntry<TElement>, new()
	{
		public TContainer this[int index] => selectionBox.EntryChain[index];

		public new TreeBox<TContainer, TElement> ListContainer => this;

/// <summary>TreeBox operation.</summary>
		public TreeBox(HudParentBase parent) : base(parent)
		{ }

/// <summary>TreeBox operation.</summary>
		public TreeBox() : base(null)
		{ }

/// <summary>Adds a .</summary>
		public void Add(TElement element) =>
			selectionBox.EntryChain.Add(element);

/// <summary>Adds a .</summary>
		public void Add(TContainer element) =>
			selectionBox.EntryChain.Add(element);

/// <summary>Adds a range.</summary>
		public void AddRange(IReadOnlyList<TContainer> newContainers) =>
			selectionBox.EntryChain.AddRange(newContainers);

/// <summary>Clear operation.</summary>
		public void Clear() =>
			selectionBox.EntryChain.Clear();

/// <summary>Find operation.</summary>
		public TContainer Find(Func<TContainer, bool> predicate) =>
			selectionBox.EntryChain.Find(predicate);

/// <summary>FindIndex operation.</summary>
		public int FindIndex(Func<TContainer, bool> predicate) =>
			selectionBox.EntryChain.FindIndex(predicate);

/// <summary>Insert operation.</summary>
		public void Insert(int index, TContainer container) =>
			selectionBox.EntryChain.Insert(index, container);

/// <summary>InsertRange operation.</summary>
		public void InsertRange(int index, IReadOnlyList<TContainer> newContainers) =>
			selectionBox.EntryChain.InsertRange(index, newContainers);

/// <summary>Removes the .</summary>
		public bool Remove(TContainer collectionElement) =>
			selectionBox.EntryChain.Remove(collectionElement);

/// <summary>Removes the .</summary>
		public bool Remove(Func<TContainer, bool> predicate) =>
			selectionBox.EntryChain.Remove(predicate);

/// <summary>Removes the at.</summary>
		public bool RemoveAt(int index) =>
			selectionBox.EntryChain.RemoveAt(index);

/// <summary>Removes the range.</summary>
		public void RemoveRange(int index, int count) =>
			selectionBox.EntryChain.RemoveRange(index, count);
	}

	public class TreeBox<TValue> : TreeBox<SelectionBoxEntryTuple<LabelElementBase, TValue>, LabelElementBase>
	{
		public new TreeBox<TValue> ListContainer => this;

/// <summary>Adds a .</summary>
		public void Add(LabelElementBase keyElement, TValue value, bool allowHighlighting = true)
		{
			var container = new SelectionBoxEntryTuple<LabelElementBase, TValue>();
			container.SetElement(keyElement);
			container.AssocMember = value;
			container.AllowHighlighting = allowHighlighting;
			selectionBox.EntryChain.Add(container);
		}
	}
}