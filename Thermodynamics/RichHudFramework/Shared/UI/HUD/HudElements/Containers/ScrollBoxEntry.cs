namespace RichHudFramework.UI
{
	public class ScrollBoxEntry<TElement> : HudElementContainer<TElement>, IScrollBoxEntry<TElement>
        where TElement : HudElementBase
    {
		public virtual bool Enabled { get; set; }

/// <summary>ScrollBoxEntry operation.</summary>
        public ScrollBoxEntry() { Enabled = true; }
    }

    public class ScrollBoxEntry : ScrollBoxEntry<HudElementBase>
    { }
}