using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework.UI.Client
{
	public enum ListControlAccessors : int
	{
		ListAccessors = 16,
	}

	public class TerminalList<T> : TerminalValue<EntryData<T>>
	{
		public override EntryData<T> Value
		{
			get { return List.Selection; }
			set { List.SetSelection(value); }
		}

		public ListBoxData<T> List { get; }


		public TerminalList() : base(MenuControls.ListControl)
		{

			var listData = GetOrSetMember(null, (int)ListControlAccessors.ListAccessors) as ApiMemberAccessor;


			List = new ListBoxData<T>(listData);
		}
	}
}