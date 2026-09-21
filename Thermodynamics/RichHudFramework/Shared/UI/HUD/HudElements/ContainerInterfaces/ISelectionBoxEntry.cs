namespace RichHudFramework.UI
{
	public interface ISelectionBoxEntry<TElement> : IScrollBoxEntry<TElement>
		 where TElement : HudElementBase
	{
		bool AllowHighlighting { get; set; }

/// <summary>Reset operation.</summary>
		void Reset();
	}

	public interface ISelectionBoxEntryTuple<TElement, TValue>
		: ISelectionBoxEntry<TElement>, IScrollBoxEntryTuple<TElement, TValue>
		where TElement : HudElementBase
	{ }
}
