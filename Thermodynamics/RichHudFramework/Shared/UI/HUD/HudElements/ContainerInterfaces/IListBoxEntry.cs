namespace RichHudFramework.UI
{
	public interface IListBoxEntry<TElement, TValue>
		: ISelectionBoxEntryTuple<TElement, TValue>
		where TElement : HudElementBase, IMinLabelElement
	{

		object GetOrSetMember(object data, int memberEnum);
	}
}