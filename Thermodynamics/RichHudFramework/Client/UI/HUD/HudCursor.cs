using System;
using VRage;
using VRageMath;
using ApiMemberAccessor = System.Func<object, int, object>;
using HudSpaceDelegate = System.Func<VRage.MyTuple<bool, float, VRageMath.MatrixD>>;

namespace RichHudFramework
{
    using CursorMembers = MyTuple<
        Func<HudSpaceDelegate, bool>,
        Func<float, HudSpaceDelegate, bool>,
        Func<ApiMemberAccessor, bool>,
        Func<ApiMemberAccessor, bool>,
        Func<ApiMemberAccessor, bool>,
        ApiMemberAccessor
    >;

    namespace UI.Client
    {
        public sealed partial class HudMain
        {
            private class HudCursor : ICursor
            {
                public bool Visible { get; private set; }

                public bool IsCaptured => (bool)GetOrSetMemberFunc(null, (int)HudCursorAccessors.IsCaptured);

                public bool IsToolTipRegistered { get; private set; }

                public Vector2 ScreenPos { get; private set; }

                public Vector3D WorldPos { get; private set; }

                public LineD WorldLine { get; private set; }

                private readonly Func<HudSpaceDelegate, bool> IsCapturingSpaceFunc;
                private readonly Func<float, HudSpaceDelegate, bool> TryCaptureHudSpaceFunc;
                private readonly Func<ApiMemberAccessor, bool> IsCapturingFunc;
                private readonly Func<ApiMemberAccessor, bool> TryCaptureFunc;
                private readonly Func<ApiMemberAccessor, bool> TryReleaseFunc;
                private readonly ApiMemberAccessor GetOrSetMemberFunc;


                public HudCursor(CursorMembers members)
                {
                    IsCapturingSpaceFunc = members.Item1;
                    TryCaptureHudSpaceFunc = members.Item2;
                    IsCapturingFunc = members.Item3;
                    TryCaptureFunc = members.Item4;
                    TryReleaseFunc = members.Item5;
                    GetOrSetMemberFunc = members.Item6;
                }


                public void Update()
                {
                    Visible = (bool)GetOrSetMemberFunc(null, (int)HudCursorAccessors.Visible);
                    ScreenPos = (Vector2)GetOrSetMemberFunc(null, (int)HudCursorAccessors.ScreenPos);
                    WorldPos = (Vector3D)GetOrSetMemberFunc(null, (int)HudCursorAccessors.WorldPos);
                    WorldLine = (LineD)GetOrSetMemberFunc(null, (int)HudCursorAccessors.WorldLine);
                    IsToolTipRegistered = (bool)GetOrSetMemberFunc(null, (int)HudCursorAccessors.IsToolTipRegistered);
                }


                public bool IsCapturingSpace(HudSpaceDelegate GetHudSpaceFunc) =>
                    IsCapturingSpaceFunc(GetHudSpaceFunc);


                public bool TryCaptureHudSpace(float depthSquared, HudSpaceDelegate GetHudSpaceFunc) =>
                    TryCaptureHudSpaceFunc(depthSquared, GetHudSpaceFunc);


                public bool IsCapturing(ApiMemberAccessor capturedElement) =>
                    IsCapturingFunc(capturedElement);


                public bool TryCapture(ApiMemberAccessor capturedElement) =>
                    TryCaptureFunc(capturedElement);


                public bool TryRelease(ApiMemberAccessor capturedElement) =>
                    TryReleaseFunc(capturedElement);


                public void RegisterToolTip(ToolTip toolTip) =>
                    GetOrSetMemberFunc(toolTip.GetToolTipFunc, (int)HudCursorAccessors.RegisterToolTip);
            }
        }
    }
}
