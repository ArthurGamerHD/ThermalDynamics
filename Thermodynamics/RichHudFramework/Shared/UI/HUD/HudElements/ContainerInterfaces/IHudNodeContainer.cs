namespace RichHudFramework.UI
{
	public interface IHudNodeContainer<TElement> where TElement : HudNodeBase
	{
		TElement Element { get; }


		void SetElement(TElement Element);
	}

	public interface IChainElementContainer<TElement> : IHudNodeContainer<TElement>
        where TElement : HudElementBase
    {
        float AlignAxisScale { get; set; }
    }
}
