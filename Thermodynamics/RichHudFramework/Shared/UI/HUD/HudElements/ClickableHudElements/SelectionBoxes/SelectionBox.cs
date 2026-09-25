using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRageMath;
using ApiMemberAccessor = System.Func<object, int, object>;
using GlyphFormatMembers = VRage.MyTuple<byte, float, VRageMath.Vector2I, VRageMath.Color>;

namespace RichHudFramework.UI
{
	using CollectionData = MyTuple<Func<int, ApiMemberAccessor>, Func<int>>;
	using RichStringMembers = MyTuple<StringBuilder, GlyphFormatMembers>;

	public class ChainSelectionBox<TContainer, TElement, TValue>
		: SelectionBox<HudChain<TContainer, TElement>, TContainer, TElement, TValue>

		where TContainer : class, IListBoxEntry<TElement, TValue>, new()
		where TElement : HudElementBase, IMinLabelElement
	{

		public ChainSelectionBox(HudParentBase parent) : base(parent)
		{ }


		public ChainSelectionBox() : base(null)
		{ }
	}

	public class ScrollSelectionBox<TContainer, TElement, TValue>
		: SelectionBox<ScrollBox<TContainer, TElement>, TContainer, TElement, TValue>

		where TContainer : class, IListBoxEntry<TElement, TValue>, new()
		where TElement : HudElementBase, IMinLabelElement
	{
		public Color Color { get { return EntryChain.Color; } set { EntryChain.Color = value; } }

		public virtual bool EnableScrolling { get { return EntryChain.EnableScrolling; } set { EntryChain.EnableScrolling = value; } }

		public virtual bool UseSmoothScrolling { get { return EntryChain.UseSmoothScrolling; } set { EntryChain.UseSmoothScrolling = value; } }

		public virtual int MinVisibleCount { get { return EntryChain.MinVisibleCount; } set { EntryChain.MinVisibleCount = value; } }

		public virtual float MinLength { get { return EntryChain.MinLength; } set { EntryChain.MinLength = value; } }

		protected override float HighlightWidth =>
			EntryChain.Size.X - Padding.X - EntryChain.ScrollBar.Width - EntryChain.Padding.X - HighlightPadding.X;


		public ScrollSelectionBox(HudParentBase parent) : base(parent)
		{ }


		public ScrollSelectionBox() : base(null)
		{ }


		protected override void HandleInput(Vector2 cursorPos)
		{
			if (listInput.KeyboardScroll)
			{
				if (listInput.HighlightIndex > EntryChain.End)
				{
					EntryChain.End = listInput.HighlightIndex;
				}

				else if (listInput.HighlightIndex < EntryChain.Start)
				{
					EntryChain.Start = listInput.HighlightIndex;
				}
			}
		}
	}

	public class SelectionBox<TChain, TContainer, TElement, TValue>
		: SelectionBoxBase<TChain, TContainer, TElement>

		where TChain : HudChain<TContainer, TElement>, new()

		where TContainer : class, IListBoxEntry<TElement, TValue>, new()
		where TElement : HudElementBase, IMinLabelElement
	{
		public new SelectionBox<TChain, TContainer, TElement, TValue> ListContainer => this;

		public Vector2 MemberPadding { get; set; }

		public virtual Vector2 ListPadding { get { return EntryChain.Padding; } set { EntryChain.Padding = value; } }

		public float LineHeight { get; set; }

		public readonly BorderBox border;

		protected readonly ObjectPool<TContainer> entryPool;


		public SelectionBox(HudParentBase parent) : base(parent)
		{

			entryPool = new ObjectPool<TContainer>(GetNewEntry, ResetEntry);
			EntryChain.SizingMode = HudChainSizingModes.FitMembersOffAxis;


			border = new BorderBox(EntryChain)
			{
				DimAlignment = DimAlignments.Size,

				Color = new Color(58, 68, 77),
				Thickness = 1f,
			};

			LineHeight = 28f;

			MemberPadding = new Vector2(20f, 6f);
		}


		public SelectionBox() : this(null)
		{ }


		public TContainer AddNew()
		{
			TContainer entry = entryPool.Get();
			EntryChain.Add(entry);
			return entry;
		}


		public TContainer Add(RichText name, TValue assocMember, bool enabled = true)
		{
			TContainer entry = entryPool.Get();

			entry.Element.TextBoard.SetText(name);
			entry.AssocMember = assocMember;
			entry.Enabled = enabled;
			EntryChain.Add(entry);

			return entry;
		}


		public void AddRange(IReadOnlyList<MyTuple<RichText, TValue, bool>> entries)
		{
			for (int n = 0; n < entries.Count; n++)
			{
				TContainer entry = entryPool.Get();

				entry.Element.TextBoard.SetText(entries[n].Item1);
				entry.AssocMember = entries[n].Item2;
				entry.Enabled = entries[n].Item3;
				EntryChain.Add(entry);
			}
		}


		public void Insert(int index, RichText name, TValue assocMember, bool enabled = true)
		{
			TContainer entry = entryPool.Get();

			entry.Element.TextBoard.SetText(name);
			entry.AssocMember = assocMember;
			entry.Enabled = enabled;
			EntryChain.Insert(index, entry);
		}


		public void RemoveAt(int index)
		{
			TContainer entry = EntryChain.Collection[index];
			EntryChain.RemoveAt(index);
			entryPool.Return(entry);
		}


		public bool Remove(TContainer entry)
		{
			if (EntryChain.Remove(entry))
			{
				entryPool.Return(entry);
				return true;
			}
			else
				return false;
		}


		public void RemoveRange(int index, int count)
		{
			entryPool.ReturnRange(EntryChain.Collection, index, count - index);
			EntryChain.RemoveRange(index, count);
		}


		public void ClearEntries()
		{
			ClearSelection();
			entryPool.ReturnRange(EntryChain.Collection);
			EntryChain.Clear();
		}


		public void SetSelection(TValue assocMember)
		{
			int index = EntryChain.FindIndex(x => assocMember.Equals(x.AssocMember));

			if (index != -1)
			{
				listInput.SetSelectionAt(index);
			}
		}


		protected virtual TContainer GetNewEntry()
		{

			var entry = new TContainer();
			entry.Element.TextBoard.Format = Format;
			entry.Element.Padding = MemberPadding;
			entry.Element.Height = LineHeight;
			entry.Element.ZOffset = 1;
			entry.Enabled = true;

			return entry;
		}


		protected virtual void ResetEntry(TContainer entry)
		{
			if (Value == entry)
				listInput.ClearSelection();

			entry.Reset();
		}


		public virtual object GetOrSetMember(object data, int memberEnum)
		{
			var member = (ListBoxAccessors)memberEnum;

			switch (member)
			{
				case ListBoxAccessors.ListMembers:
					return new CollectionData
					(
						x => EntryChain.Collection[x].GetOrSetMember,
						() => EntryChain.Collection.Count
					 );
				case ListBoxAccessors.Add:
					{
						if (data is MyTuple<List<RichStringMembers>, TValue>)
						{
							var entryData = (MyTuple<List<RichStringMembers>, TValue>)data;
							return (ApiMemberAccessor)Add(new RichText(entryData.Item1), entryData.Item2).GetOrSetMember;
						}
						else
						{
							var entryData = (MyTuple<IList<RichStringMembers>, TValue>)data;
							var stringList = entryData.Item1 as List<RichStringMembers>;
							return (ApiMemberAccessor)Add(new RichText(stringList), entryData.Item2).GetOrSetMember;
						}
					}
				case ListBoxAccessors.Selection:
					{
						if (data == null)
							return Value;
						else
							SetSelection(data as TContainer);

						break;
					}
				case ListBoxAccessors.SelectionIndex:
					{
						if (data == null)
							return SelectionIndex;
						else
							SetSelectionAt((int)data); break;
					}
				case ListBoxAccessors.SetSelectionAtData:
					SetSelection((TValue)data); break;
				case ListBoxAccessors.Insert:
					{
						var entryData = (MyTuple<int, List<RichStringMembers>, TValue>)data;
						Insert(entryData.Item1, new RichText(entryData.Item2), entryData.Item3);
						break;
					}
				case ListBoxAccessors.Remove:

					return Remove(data as TContainer);
				case ListBoxAccessors.RemoveAt:
					RemoveAt((int)data); break;
				case ListBoxAccessors.ClearEntries:
					ClearEntries(); break;
			}

			return null;
		}
	}
}