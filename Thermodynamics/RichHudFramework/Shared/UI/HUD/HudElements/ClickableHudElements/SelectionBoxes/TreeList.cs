using System.Collections.Generic;
using VRage;

namespace RichHudFramework.UI
{
	public class TreeList<TValue> : TreeList<ListBoxEntry<TValue>, Label, TValue>
	{

		public TreeList(HudParentBase parent) : base(parent)
		{ }


		public TreeList() : base(null)
		{ }
	}

	public class TreeList<TElement, TValue> : TreeList<ListBoxEntry<TElement, TValue>, TElement, TValue>

		where TElement : HudElementBase, IMinLabelElement, new()
	{

		public TreeList(HudParentBase parent) : base(parent)
		{ }


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

		where TContainer : class, IListBoxEntry<TElement, TValue>, new()
		where TElement : HudElementBase, IMinLabelElement
	{
		public float LineHeight { get { return selectionBox.LineHeight; } set { selectionBox.LineHeight = value; } }

		public new TreeList<TContainer, TElement, TValue> ListContainer => this;


		public TreeList(HudParentBase parent) : base(parent)
		{
			selectionBox.border.Visible = false;
			selectionBox.EntryChain.SizingMode = HudChainSizingModes.FitMembersOffAxis;
		}


		public TreeList() : this(null)
		{ }


		public TContainer Add(RichText name, TValue assocMember, bool enabled = true) =>
			selectionBox.Add(name, assocMember, enabled);


		public void AddRange(IReadOnlyList<MyTuple<RichText, TValue, bool>> entries) =>
			selectionBox.AddRange(entries);


		public void Insert(int index, RichText name, TValue assocMember, bool enabled = true) =>
			selectionBox.Insert(index, name, assocMember, enabled);


		public void RemoveAt(int index) =>
			selectionBox.RemoveAt(index);


		public void RemoveRange(int index, int count) =>
			selectionBox.RemoveRange(index, count);


		public void ClearEntries() =>
			selectionBox.ClearEntries();


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