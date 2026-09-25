using System;
using System.Collections.Generic;

namespace RichHudFramework.UI
{
	public class TreeBox<TContainer, TElement> : TreeBoxBase<TContainer, TElement>
		where TElement : HudElementBase, IMinLabelElement

		where TContainer : class, ISelectionBoxEntry<TElement>, new()
	{
		public TContainer this[int index] => selectionBox.EntryChain[index];

		public new TreeBox<TContainer, TElement> ListContainer => this;


		public TreeBox(HudParentBase parent) : base(parent)
		{ }


		public TreeBox() : base(null)
		{ }


		public void Add(TElement element) =>
			selectionBox.EntryChain.Add(element);


		public void Add(TContainer element) =>
			selectionBox.EntryChain.Add(element);


		public void AddRange(IReadOnlyList<TContainer> newContainers) =>
			selectionBox.EntryChain.AddRange(newContainers);


		public void Clear() =>
			selectionBox.EntryChain.Clear();


		public TContainer Find(Func<TContainer, bool> predicate) =>
			selectionBox.EntryChain.Find(predicate);


		public int FindIndex(Func<TContainer, bool> predicate) =>
			selectionBox.EntryChain.FindIndex(predicate);


		public void Insert(int index, TContainer container) =>
			selectionBox.EntryChain.Insert(index, container);


		public void InsertRange(int index, IReadOnlyList<TContainer> newContainers) =>
			selectionBox.EntryChain.InsertRange(index, newContainers);


		public bool Remove(TContainer collectionElement) =>
			selectionBox.EntryChain.Remove(collectionElement);


		public bool Remove(Func<TContainer, bool> predicate) =>
			selectionBox.EntryChain.Remove(predicate);


		public bool RemoveAt(int index) =>
			selectionBox.EntryChain.RemoveAt(index);


		public void RemoveRange(int index, int count) =>
			selectionBox.EntryChain.RemoveRange(index, count);
	}

	public class TreeBox<TValue> : TreeBox<SelectionBoxEntryTuple<LabelElementBase, TValue>, LabelElementBase>
	{
		public new TreeBox<TValue> ListContainer => this;


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