using RichHudFramework.UI.Client;
using System;

namespace RichHudFramework
{
/// <summary>EventHandler operation.</summary>
	public delegate void EventHandler(object sender, EventArgs e);

	namespace UI
	{
		public enum HudInputMode : int
		{
			NoInput = 0,

			CursorOnly = 1,

			Full = 2
		}

		public enum HudMainAccessors : int
		{
			ScreenWidth = 1,

			ScreenHeight = 2,

			AspectRatio = 3,

			ResScale = 4,

			Fov = 5,

			FovScale = 6,

			PixelToWorldTransform = 7,

			ClipBoard = 8,

			UiBkOpacity = 9,

			EnableCursor = 10,

			RefreshDrawList = 11,

			GetUpdateAccessorsOld = 12,

			GetFocusOffset = 13,

			GetPixelSpaceFunc = 14,

			GetPixelSpaceOriginFunc = 15,

			GetInputFocus = 16,

			TreeRefreshRate = 17,

			InputMode = 18,

			SetBeforeDrawCallback = 19,

			SetAfterDrawCallback = 20,

			SetBeforeInputCallback = 21,

			SetAfterInputCallback = 22,

			ClientRootNode = 23,
		}

		public enum ListBoxEntryAccessors : int
		{
			Name = 1,

			Enabled = 2,

			AssocObject = 3,

			ID = 4,
		}

		public enum ListBoxAccessors : int
		{
			ListMembers = 1,

			Add = 2,

			Selection = 3,

			SelectionIndex = 4,

			SetSelectionAtData = 5,

			Insert = 6,

			Remove = 7,

			RemoveAt = 8,

			ClearEntries = 9
		}
	}
}

namespace RichHudFramework.UI.Server { }
namespace RichHudFramework.UI.Rendering.Server { }