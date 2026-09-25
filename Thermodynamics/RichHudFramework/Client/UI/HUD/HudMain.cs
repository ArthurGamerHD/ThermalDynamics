using RichHudFramework.UI.Rendering;
using System;
using System.Collections.Generic;
using VRage;
using VRageMath;
using ApiMemberAccessor = System.Func<object, int, object>;
using FloatProp = VRage.MyTuple<System.Func<float>, System.Action<float>>;
using HudSpaceDelegate = System.Func<VRage.MyTuple<bool, float, VRageMath.MatrixD>>;
using RichStringMembers = VRage.MyTuple<System.Text.StringBuilder, VRage.MyTuple<byte, float, VRageMath.Vector2I, VRageMath.Color>>;
using Vec2Prop = VRage.MyTuple<System.Func<VRageMath.Vector2>, System.Action<VRageMath.Vector2>>;

namespace RichHudFramework
{
    using Client;
    using Internal;
    using CursorMembers = MyTuple<
        Func<HudSpaceDelegate, bool>,
        Func<float, HudSpaceDelegate, bool>,
        Func<ApiMemberAccessor, bool>,
        Func<ApiMemberAccessor, bool>,
        Func<ApiMemberAccessor, bool>,
        ApiMemberAccessor
    >;
    using TextBuilderMembers = MyTuple<
        MyTuple<Func<int, int, object>, Func<int>>,
        Func<Vector2I, int, object>,
        ApiMemberAccessor,
        Action<IList<RichStringMembers>, Vector2I>,
        Action<IList<RichStringMembers>>,
        Action
    >;

    namespace UI
    {
        using static NodeConfigIndices;
        using TextBoardMembers = MyTuple<
            TextBuilderMembers,
            FloatProp,
            Func<Vector2>,
            Func<Vector2>,
            Vec2Prop,
            Action<BoundingBox2, BoundingBox2, MatrixD[]>
        >;

        namespace Client
        {
            using HudClientMembers = MyTuple<
                CursorMembers,
                Func<TextBoardMembers>,
                ApiMemberAccessor,
                Action
            >;

            public sealed partial class HudMain : RichHudClient.ApiModule
            {
                public static HudParentBase Root
                {
                    get
                    {
                        if (Instance == null)
                            Init();

                        return Instance._root;
                    }
                }

                public static HudParentBase HighDpiRoot
                {
                    get
                    {
                        if (Instance == null)
                            Init();

                        return Instance._highDpiRoot;
                    }
                }

                public static ICursor Cursor
                {
                    get
                    {
                        if (Instance == null)
                            Init();

                        return Instance._cursor;
                    }
                }

                public static RichText ClipBoard
                {
                    get
                    {
                        if (Instance == null)
                            Init();

                        object value = Instance.GetOrSetMemberFunc(null, (int)HudMainAccessors.ClipBoard);

                        if (value != null)
                            return new RichText(value as List<RichStringMembers>);
                        else

                            return default(RichText);
                    }
                    set
                    {
                        if (Instance == null)
                            Init();

                        Instance.GetOrSetMemberFunc(value.apiData, (int)HudMainAccessors.ClipBoard);
                    }
                }

                public static float ResScale { get; private set; }

                public static MatrixD PixelToWorld => PixelToWorldRef[0];

                public static MatrixD[] PixelToWorldRef { get; private set; }

                public static float ScreenWidth { get; private set; }

                public static float ScreenHeight { get; private set; }

                public static Vector2 ScreenDim { get; private set; }

                public static Vector2 ScreenDimHighDPI { get; private set; }

                public static float AspectRatio { get; private set; }

                public static float Fov { get; private set; }

                public static float FovScale { get; private set; }

                public static float UiBkOpacity { get; private set; }

                public static bool EnableCursor { get; set; }

                public static HudInputMode InputMode { get; private set; }

                public static HudMain Instance { get; private set; }

                public readonly HudParentBase _root;

                private readonly HudParentBase _highDpiRoot;
                private readonly HudCursor _cursor;
                private bool enableCursorLast, enableCursorTemp;

                private readonly Func<TextBoardMembers> GetTextBoardDataFunc;
                private readonly ApiMemberAccessor GetOrSetMemberFunc;
                private readonly Action UnregisterAction;


                private HudMain() : base(ApiModuleTypes.HudMain, false, true)
                {
                    if (Instance != null)
                        throw new Exception("Only one instance of HudMain can exist at any given time!");

                    Instance = this;
                    var members = (HudClientMembers)GetApiData();


                    _cursor = new HudCursor(members.Item1);
                    GetTextBoardDataFunc = members.Item2;
                    GetOrSetMemberFunc = members.Item3;
                    UnregisterAction = members.Item4;

                    PixelToWorldRef = new MatrixD[1];

                    _root = new HudClientRoot();

                    _highDpiRoot = new HighDpiClientRoot();

                    GetOrSetMemberFunc(_root.DataHandle, (int)HudMainAccessors.ClientRootNode);
                    GetOrSetMemberFunc(new Action(() => ExceptionHandler.Run(BeforeMasterDraw)), (int)HudMainAccessors.SetBeforeDrawCallback);

                    UpdateCache();
                }


                public static void Init()
                {
                    BillBoardUtils.Init();

                    if (Instance == null)

                        new HudMain();
                }


                private void BeforeMasterDraw()
                {
                    UpdateCache();
                    _cursor.Update();
                }


                public override void Close()
                {
                    UnregisterAction?.Invoke();
                    Instance = null;
                }


                private void UpdateCache()
                {
                    ScreenWidth = (float)GetOrSetMemberFunc(null, (int)HudMainAccessors.ScreenWidth);
                    ScreenHeight = (float)GetOrSetMemberFunc(null, (int)HudMainAccessors.ScreenHeight);
                    AspectRatio = (float)GetOrSetMemberFunc(null, (int)HudMainAccessors.AspectRatio);
                    ResScale = (float)GetOrSetMemberFunc(null, (int)HudMainAccessors.ResScale);
                    Fov = (float)GetOrSetMemberFunc(null, (int)HudMainAccessors.Fov);
                    FovScale = (float)GetOrSetMemberFunc(null, (int)HudMainAccessors.FovScale);
                    PixelToWorldRef[0] = (MatrixD)GetOrSetMemberFunc(null, (int)HudMainAccessors.PixelToWorldTransform);
                    UiBkOpacity = (float)GetOrSetMemberFunc(null, (int)HudMainAccessors.UiBkOpacity);
                    InputMode = (HudInputMode)GetOrSetMemberFunc(null, (int)HudMainAccessors.InputMode);


                    ScreenDim = new Vector2(ScreenWidth, ScreenHeight);
                    ScreenDimHighDPI = ScreenDim / ResScale;

                    GetOrSetMemberFunc(EnableCursor | enableCursorTemp, (int)HudMainAccessors.EnableCursor);
                    enableCursorLast = EnableCursor;
                    enableCursorTemp = false;
                }


                public static void EnableCursorTemp()
                {
                    if (Instance == null)
                        Init();

                    Instance.enableCursorTemp = true;
                }


                public static byte GetFocusOffset(Action<byte> LoseFocusCallback)
                {
                    if (Instance == null)
                        Init();

                    return (byte)Instance.GetOrSetMemberFunc(LoseFocusCallback, (int)HudMainAccessors.GetFocusOffset);
                }


                public static void GetInputFocus(IFocusHandler handler)
                {
                    if (Instance == null)
                        Init();

                    Instance.GetOrSetMemberFunc(new Action(handler.ReleaseFocus), (int)HudMainAccessors.GetInputFocus);
                }


                public static TextBoardMembers GetTextBoardData()
                {
                    if (Instance == null)
                        Init();

                    return Instance.GetTextBoardDataFunc();
                }

                private class HudClientRoot : HudParentBase, IReadOnlyHudSpaceNode
                {
                    public bool DrawCursorInHudSpace { get; }

                    public Vector3 CursorPos { get; private set; }

                    public HudSpaceDelegate GetHudSpaceFunc { get; }

                    public MatrixD PlaneToWorld => PlaneToWorldRef[0];

                    public MatrixD[] PlaneToWorldRef { get; }

                    public Func<Vector3D> GetNodeOriginFunc
                    {
                        get { return DataHandle[0].Item2[0]; }
                        private set { DataHandle[0].Item2[0] = value; }
                    }

                    public bool IsInFront { get; }

                    public bool IsFacingCamera { get; }


                    public HudClientRoot()
                    {
                        DrawCursorInHudSpace = true;
                        HudSpace = this;
                        IsInFront = true;
                        IsFacingCamera = true;
                        PlaneToWorldRef = PixelToWorldRef;

                        GetHudSpaceFunc = Instance.GetOrSetMemberFunc(null, (int)HudMainAccessors.GetPixelSpaceFunc) as HudSpaceDelegate;
                        GetNodeOriginFunc = Instance.GetOrSetMemberFunc(null, (int)HudMainAccessors.GetPixelSpaceOriginFunc) as Func<Vector3D>;
                        _config[StateID] |= (uint)(HudElementStates.CanUseCursor | HudElementStates.IsSpaceNode);
                    }


                    protected override void Layout()
                    {

                        CursorPos = new Vector3(Cursor.ScreenPos.X, Cursor.ScreenPos.Y, 0f);
                        HudElementBase.ElementUtils.UpdateRootAnchoring(ScreenDim, children);
                    }
                }

                private class HighDpiClientRoot : ScaledSpaceNode
                {

                    public HighDpiClientRoot() : base(Root)
                    {
                        UpdateScaleFunc = () => ResScale;
                    }


                    protected override void Layout()
                    {
                        base.Layout();
                        HudElementBase.ElementUtils.UpdateRootAnchoring(ScreenDimHighDPI, children);
                    }
                }
            }
        }
    }

    namespace UI.Server
    { }
}