using System.Collections.Generic;

namespace RichHudFramework.UI
{
	public interface IEntryBox<TContainer, TElement> : IEnumerable<TContainer>, IValueControl<TContainer>
/// <summary>new operation.</summary>
		where TContainer : IScrollBoxEntry<TElement>, new()
		where TElement : HudElementBase, IMinLabelElement
	{
        IReadOnlyList<TContainer> EntryList { get; }

		new TContainer Value { get; }
	}
}