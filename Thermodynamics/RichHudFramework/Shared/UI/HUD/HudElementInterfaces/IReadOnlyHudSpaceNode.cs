using System;
using VRageMath;
using HudSpaceDelegate = System.Func<VRage.MyTuple<bool, float, VRageMath.MatrixD>>;

namespace RichHudFramework
{
	namespace UI
	{
		public interface IReadOnlyHudSpaceNode : IReadOnlyHudParent
		{
			Vector3 CursorPos { get; }

			HudSpaceDelegate GetHudSpaceFunc { get; }

			MatrixD PlaneToWorld { get; }

			MatrixD[] PlaneToWorldRef { get; }

			Func<Vector3D> GetNodeOriginFunc { get; }

			bool DrawCursorInHudSpace { get; }

			bool IsInFront { get; }

			bool IsFacingCamera { get; }
		}
	}
}
