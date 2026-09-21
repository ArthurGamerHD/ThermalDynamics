using System.Collections.Generic;
using VRage;

namespace RichHudFramework.UI
{
	public class TreeList<TValue> : TreeList<ListBoxEntry<TValue>, Label, TValue>
	{
/// <summary>TreeList operation.</summary>
		public TreeList(HudParentBase parent) : base(parent)
		{ }

/// <summary>TreeList operation.</summary>
		public TreeList() : base(null)
		{ }
	}

	public class TreeList<TElement, TValue> : TreeList<ListBoxEntry<TElement, TValue>, TElement, TValue>
/// <summary>new operation.</summary>
		where TElement : HudElementBase, IMinLabelElement, new()
	{
/// <summary>TreeList operation.</summary>
		public TreeList(HudParentBase parent) : base(parent)
		{ }

/// <summary>TreeList operation.</summary>
		public TreeList() : base(null)
		{ }
	}

	public class TreeList<TContainer, TElement, TValue>
		: TreeBoxBase<
			ChainSelectionBox<TContainer, TElement, TValue>,
			HudChain<TContainer, TElement>,
			TContainer,
			TElement
		>
/// <summary>new operation.</summary>
		where TContainer : class, IListBoxEntry<TElement, TValue>, new()
		where TElement : HudElementBase, IMinLabelElement
	{
		public float LineHeight { get { return selectionBox.LineHeight; } set { selectionBox.LineHeight = value; } }

		public new TreeList<TContainer, TElement, TValue> ListContainer => this;

/// <summary>TreeList operation.</summary>
		public TreeList(HudParentBase parent) : base(parent)
		{
			selectionBox.border.Visible = false;
			selectionBox.EntryChain.SizingMode = HudChainSizingModes.FitMembersOffAxis;
		}

/// <summary>TreeList operation.</summary>
		public TreeList() : this(null)
		{ }

/// <summary>Adds a .</summary>
		public TContainer Add(RichText name, TValue assocMember, bool enabled = true) =>
			selectionBox.Add(name, assocMember, enabled);

/// <summary>Adds a range.</summary>
		public void AddRange(IReadOnlyList<MyTuple<RichText, TValue, bool>> entries) =>
			selectionBox.AddRange(entries);

/// <summary>Insert operation.</summary>
		public void Insert(int index, RichText name, TValue assocMember, bool enabled = true) =>
			selectionBox.Insert(index, name, assocMember, enabled);

/// <summary>Removes the at.</summary>
		public void RemoveAt(int index) =>
			selectionBox.RemoveAt(index);

/// <summary>Removes the range.</summary>
		public void RemoveRange(int index, int count) =>
			selectionBox.RemoveRange(index, count);

/// <summary>ClearEntries operation.</summary>
		public void ClearEntries() =>
			selectionBox.ClearEntries();

/// <summary>Sets the selection.</summary>
		public void SetSelection(TValue assocMember)
		{
			int index = selectionBox.EntryChain.FindIndex(x => assocMember.Equals(x.AssocMember));

			if (index != -1)
			{
				selectionBox.SetSelectionAt(index);
			}
		}
	}
}