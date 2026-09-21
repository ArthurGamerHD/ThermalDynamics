using VRageMath;

namespace RichHudFramework.UI
{
    public class ListBox<TValue> : ListBox<ListBoxEntry<TValue>, Label, TValue>
    {
/// <summary>ListBox operation.</summary>
        public ListBox(HudParentBase parent) : base(parent)
        { }

/// <summary>ListBox operation.</summary>
        public ListBox() : base(null)
        { }
    }

    public class ListBox<TContainer, TElement, TValue>
        : ScrollSelectionBox<TContainer, TElement, TValue>, IClickableElement
/// <summary>new operation.</summary>
        where TContainer : class, IListBoxEntry<TElement, TValue>, new()
        where TElement : HudElementBase, IMinLabelElement
    {
        public Color BarColor { get { return EntryChain.BarColor; } set { EntryChain.BarColor = value; } }

        public Color BarHighlight { get { return EntryChain.BarHighlight; } set { EntryChain.BarHighlight = value; } }

        public Color SliderColor { get { return EntryChain.SliderColor; } set { EntryChain.SliderColor = value; } }

        public Color SliderHighlight { get { return EntryChain.SliderHighlight; } set { EntryChain.SliderHighlight = value; } }

        protected override Vector2I ListRange => EntryChain.ClipRange;

        protected override Vector2 ListSize
        {
            get
            {
                Vector2 listSize = EntryChain.Size;
                listSize.X -= EntryChain.ScrollBar.Width;
                return listSize;
            }
        }

        protected override Vector2 ListPos
        {
            get
            {
                Vector2 listPos = EntryChain.Position;
                listPos.X -= EntryChain.ScrollBar.Width;

                return listPos;
            }
        }

/// <summary>ListBox operation.</summary>
        public ListBox(HudParentBase parent) : base(parent)
        {
/// <summary>Vector2 operation.</summary>
            EntryChain.Padding = new Vector2(0f, 8f);
        }

/// <summary>ListBox operation.</summary>
        public ListBox() : this(null)
        { }
    }
}