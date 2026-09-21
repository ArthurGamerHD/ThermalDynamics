using VRageMath;
using ApiMemberAccessor = System.Func<object, int, object>;
using HudSpaceDelegate = System.Func<VRage.MyTuple<bool, float, VRageMath.MatrixD>>;

namespace RichHudFramework
{
	namespace UI
	{
		public enum HudCursorAccessors : int
		{
			Visible = 0,

			IsCaptured = 1,

			ScreenPos = 2,

			WorldPos = 3,

			WorldLine = 4,

			RegisterToolTip = 5,

			IsToolTipRegistered = 6,
		}

		public interface ICursor
		{
			bool Visible { get; }

			bool IsCaptured { get; }

			bool IsToolTipRegistered { get; }

			Vector2 ScreenPos { get; }

			Vector3D WorldPos { get; }

			LineD WorldLine { get; }

/// <summary>IsCapturingSpace operation.</summary>
			bool IsCapturingSpace(HudSpaceDelegate GetHudSpaceFunc);

/// <summary>TryCaptureHudSpace operation.</summary>
			bool TryCaptureHudSpace(float depthSquared, HudSpaceDelegate GetHudSpaceFunc);

/// <summary>IsCapturing operation.</summary>
			bool IsCapturing(ApiMemberAccessor capturedElement);

/// <summary>TryCapture operation.</summary>
			bool TryCapture(ApiMemberAccessor capturedElement);

/// <summary>TryRelease operation.</summary>
			bool TryRelease(ApiMemberAccessor capturedElement);

/// <summary>Registers the API and message handler.</summary>
			void RegisterToolTip(ToolTip toolTip);
		}
	}
}