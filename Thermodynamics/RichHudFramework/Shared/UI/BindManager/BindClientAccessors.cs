using System;

namespace RichHudFramework.UI
{
	[Flags]
	public enum SeBlacklistModes : int
	{
		None = 0,

		Mouse = 1 << 0,

		AllKeys = 1 << 1 | Mouse,

		CameraRot = 1 << 2,

		MouseAndCam = Mouse | CameraRot,

		Full = AllKeys | CameraRot,

		Chat = 1 << 3,

		FullWithChat = Full | Chat
	}

	public enum BindClientAccessors : int
    {
        GetOrCreateGroup = 1,

        GetBindGroup = 2,

        GetComboIndices = 3,

        GetControlByName = 4,

        ClearBindGroups = 5,

        Unload = 6,

        RequestBlacklistMode = 7,

        IsChatOpen = 8,

        GetControlName = 9,

        GetControlNames = 10,
    }
}