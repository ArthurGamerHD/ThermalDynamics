using System;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using Client;
	using ControlContainerMembers = MyTuple<
		ApiMemberAccessor,
		MyTuple<object, Func<int>>,
		object
	>;
	using ControlMembers = MyTuple<
		ApiMemberAccessor,
		object
	>;

	namespace UI.Client
	{
		using SettingsMenuMembers = MyTuple<
			ApiMemberAccessor,
			ControlContainerMembers,
			Func<int, ControlMembers>,
			Func<int, ControlContainerMembers>,
			Func<int, ControlMembers>
		>;

		public sealed partial class RichHudTerminal : RichHudClient.ApiModule
		{
			public static IModControlRoot Root => Instance.menuRoot;

			public static bool Open => (bool)Instance.GetOrSetMembersFunc(null, (int)TerminalAccessors.GetMenuOpen);

			public static RichHudTerminal Instance
			{

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


			private RichHudTerminal() : base(ApiModuleTypes.SettingsMenu, false, true)
			{
				var data = (SettingsMenuMembers)GetApiData();

				GetOrSetMembersFunc = data.Item1;
				GetNewControlFunc = data.Item3;
				GetNewContainerFunc = data.Item4;
				GetNewPageFunc = data.Item5;

				GetNewPageCategoryFunc =
					GetOrSetMembersFunc(null, (int)TerminalAccessors.GetNewPageCategoryFunc) as Func<ControlContainerMembers>;


				menuRoot = new ModControlRoot(data.Item2);
			}


			private static void Init()
			{
				if (_instance == null)
				{

					_instance = new RichHudTerminal();
				}
			}


			public static void ToggleMenu()
			{
				if (_instance == null)
					Init();

				_instance.GetOrSetMembersFunc(null, (int)TerminalAccessors.ToggleMenu);
			}


			public static void OpenMenu()
			{
				if (_instance == null)
					Init();

				_instance.GetOrSetMembersFunc(null, (int)TerminalAccessors.OpenMenu);
			}


			public static void CloseMenu()
			{
				if (_instance == null)
					Init();

				_instance.GetOrSetMembersFunc(null, (int)TerminalAccessors.CloseMenu);
			}


			public static void OpenToPage(TerminalPageBase newPage)
			{
				_instance.GetOrSetMembersFunc(new MyTuple<object, object>(_instance.menuRoot.ID, newPage.ID), (int)TerminalAccessors.OpenToPage);
			}


			public static void SetPage(TerminalPageBase newPage)
			{
				_instance.GetOrSetMembersFunc(new MyTuple<object, object>(_instance.menuRoot.ID, newPage.ID), (int)TerminalAccessors.SetPage);
			}


			public override void Close()
			{
				_instance = null;
			}


			public ControlMembers GetNewMenuControl(MenuControls controlEnum) =>
				Instance.GetNewControlFunc((int)controlEnum);


			public ControlContainerMembers GetNewMenuTile() =>
				Instance.GetNewContainerFunc((int)ControlContainers.Tile);


			public ControlContainerMembers GetNewMenuCategory() =>
				Instance.GetNewContainerFunc((int)ControlContainers.Category);


			public ControlMembers GetNewMenuPage(ModPages pageEnum) =>
				Instance.GetNewPageFunc((int)pageEnum);


			public ControlContainerMembers GetNewPageCategory() =>
				Instance.GetNewPageCategoryFunc();
		}
	}
}