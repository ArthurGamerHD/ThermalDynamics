namespace RichHudFramework.UI
{
	public class ScrollBoxEntry<TElement> : HudElementContainer<TElement>, IScrollBoxEntry<TElement>
        where TElement : HudElementBase
    {
		public virtual bool Enabled { get; set; }


        public ScrollBoxEntry() { Enabled = true; }
    }

    public class ScrollBoxEntry : ScrollBoxEntry<HudElementBase>
    { }
}