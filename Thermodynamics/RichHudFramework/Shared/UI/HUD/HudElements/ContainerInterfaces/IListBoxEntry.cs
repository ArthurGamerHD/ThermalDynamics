namespace RichHudFramework.UI
{
	public interface IListBoxEntry<TElement, TValue>
		: ISelectionBoxEntryTuple<TElement, TValue>
		where TElement : HudElementBase, IMinLabelElement
	{
/// <summary>Returns the orsetmember.</summary>
		object GetOrSetMember(object data, int memberEnum);
	}
}