using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework.UI.Client
{
	public class TerminalDropdown<T> : TerminalValue<EntryData<T>>
	{
		public override EntryData<T> Value
		{
			get { return List.Selection; }
			set { List.SetSelection(value); }
		}

		public ListBoxData<T> List { get; }

/// <summary>TerminalDropdown operation.</summary>
		public TerminalDropdown() : base(MenuControls.DropdownControl)
		{
/// <summary>Returns the orsetmember.</summary>
			var listData = GetOrSetMember(null, (int)ListControlAccessors.ListAccessors) as ApiMemberAccessor;

/// <summary>ListBoxData operation.</summary>
			List = new ListBoxData<T>(listData);
		}
	}
}