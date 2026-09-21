using System;

namespace RichHudFramework
{
	namespace UI
	{
		[Flags]
		public enum HudElementStates : uint
		{
			None = 0x0,

			IsVisible = 1 << 0,

			WasParentVisible = 1 << 1,

			IsRegistered = 1 << 2,

			CanUseCursor = 1 << 3,

			CanShareCursor = 1 << 4,

			IsMousedOver = 1 << 5,

			IsMouseInBounds = 1 << 6,

			IsStructureStale = 1 << 7,

			IsMasked = 1 << 8,

			IsMasking = 1 << 9,

			IsSelectivelyMasked = 1 << 10,

			CanIgnoreMasking = 1 << 11,

			IsInputEnabled = 1 << 12,

			WasParentInputEnabled = 1 << 13,

			IsSpaceNode = 1 << 15,

			IsSpaceNodeReady = 1 << 16,

			IsDisjoint = 1 << 17,

			IsInactiveLeaf = 1 << 18,

			IsInputHandlerCustom = 1 << 19,

			IsLayoutCustom = 1 << 20,
		}

		public enum HudElementAccessors : int
		{
			ModName = 0,

			GetType = 1,

			ZOffset = 2,

			FullZOffset = 3,

			Position = 4,

			Size = 5,

			LocalCursorPos = 6,

			DrawCursorInHudSpace = 7,

			GetHudSpaceFunc = 8,

			NodeOrigin = 9,

			PlaneToWorld = 10,

			IsInFront = 11,

			IsFacingCamera = 12,
		}

		public interface IReadOnlyHudParent
		{
			IReadOnlyHudSpaceNode HudSpace { get; }

			bool Visible { get; }

			bool InputEnabled { get; }

			sbyte ZOffset { get; }
		}
	}
}