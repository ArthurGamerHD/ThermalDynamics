using System;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using Client;
	using ControlContainerMembers = MyTuple<
		ApiMemberAccessor, // GetOrSetMember,
		MyTuple<object, Func<int>>, // Member List
		object // ID
	>;
	using ControlMembers = MyTuple<
		ApiMemberAccessor, // GetOrSetMember
		object // ID
	>;

	namespace UI.Client
	{
		using SettingsMenuMembers = MyTuple<
			ApiMemberAccessor, // GetOrSetMembers
			ControlContainerMembers, // MenuRoot
			Func<int, ControlMembers>, // GetNewControl
			Func<int, ControlContainerMembers>, // GetNewContainer
			Func<int, ControlMembers> // GetNewModPage
		>;

		public sealed partial class RichHudTerminal : RichHudClient.ApiModule
		{
			public static IModControlRoot Root => Instance.menuRoot;

			public static bool Open => (bool)Instance.GetOrSetMembersFunc(null, (int)TerminalAccessors.GetMenuOpen);

			public static RichHudTerminal Instance
			{
/// <summary>Init operation.</summary>
				get { Init(); return _instance; }
				set { _instance = value; }
			}
			private static RichHudTerminal _instance;

			private readonly ModControlRoot menuRoot;
			private readonly ApiMemberAccessor GetOrSetMembersFunc;
			private readonly Func<int, ControlMembers> GetNewControlFunc;
			private readonly Func<int, ControlContainerMembers> GetNewContainerFunc;
			private readonly Func<int, ControlMembers> GetNewPageFunc;
			private readonly Func<ControlContainerMembers> GetNewPageCategoryFunc;

/// <summary>RichHudTerminal operation.</summary>
			private RichHudTerminal() : base(ApiModuleTypes.SettingsMenu, false, true)
			{
				var data = (SettingsMenuMembers)GetApiData();

				GetOrSetMembersFunc = data.Item1;
				GetNewControlFunc = data.Item3;
				GetNewContainerFunc = data.Item4;
				GetNewPageFunc = data.Item5;

				GetNewPageCategoryFunc =
					GetOrSetMembersFunc(null, (int)TerminalAccessors.GetNewPageCategoryFunc) as Func<ControlContainerMembers>;

/// <summary>ModControlRoot operation.</summary>
				menuRoot = new ModControlRoot(data.Item2);
			}

/// <summary>Init operation.</summary>
			private static void Init()
			{
				if (_instance == null)
				{
/// <summary>RichHudTerminal operation.</summary>
					_instance = new RichHudTerminal();
				}
			}

/// <summary>ToggleMenu operation.</summary>
			public static void ToggleMenu()
			{
				if (_instance == null)
					Init();

				_instance.GetOrSetMembersFunc(null, (int)TerminalAccessors.ToggleMenu);
			}

/// <summary>OpenMenu operation.</summary>
			public static void OpenMenu()
			{
				if (_instance == null)
					Init();

				_instance.GetOrSetMembersFunc(null, (int)TerminalAccessors.OpenMenu);
			}

/// <summary>CloseMenu operation.</summary>
			public static void CloseMenu()
			{
				if (_instance == null)
					Init();

				_instance.GetOrSetMembersFunc(null, (int)TerminalAccessors.CloseMenu);
			}

/// <summary>OpenToPage operation.</summary>
			public static void OpenToPage(TerminalPageBase newPage)
			{
				_instance.GetOrSetMembersFunc(new MyTuple<object, object>(_instance.menuRoot.ID, newPage.ID), (int)TerminalAccessors.OpenToPage);
			}

/// <summary>Sets the page.</summary>
			public static void SetPage(TerminalPageBase newPage)
			{
				_instance.GetOrSetMembersFunc(new MyTuple<object, object>(_instance.menuRoot.ID, newPage.ID), (int)TerminalAccessors.SetPage);
			}

/// <summary>Close operation.</summary>
			public override void Close()
			{
				_instance = null;
			}

/// <summary>Returns the newmenucontrol.</summary>
			public ControlMembers GetNewMenuControl(MenuControls controlEnum) =>
				Instance.GetNewControlFunc((int)controlEnum);

/// <summary>Returns the newmenutile.</summary>
			public ControlContainerMembers GetNewMenuTile() =>
				Instance.GetNewContainerFunc((int)ControlContainers.Tile);

/// <summary>Returns the newmenucategory.</summary>
			public ControlContainerMembers GetNewMenuCategory() =>
				Instance.GetNewContainerFunc((int)ControlContainers.Category);

/// <summary>Returns the newmenupage.</summary>
			public ControlMembers GetNewMenuPage(ModPages pageEnum) =>
				Instance.GetNewPageFunc((int)pageEnum);

/// <summary>Returns the newpagecategory.</summary>
			public ControlContainerMembers GetNewPageCategory() =>
				Instance.GetNewPageCategoryFunc();
		}
	}
}