using Sandbox.ModAPI;
using System;
using VRage;
using VRageMath;
using HudSpaceDelegate = System.Func<VRage.MyTuple<bool, float, VRageMath.MatrixD>>;

namespace RichHudFramework
{
	namespace UI
	{
		using Client;
		using Server;
		using static NodeConfigIndices;

		public abstract class HudSpaceNodeBase : HudNodeBase, IReadOnlyHudSpaceNode
		{
			public override IReadOnlyHudSpaceNode HudSpace => this;

			public MatrixD PlaneToWorld => PlaneToWorldRef[0];

			public MatrixD[] PlaneToWorldRef { get; }

			public Vector3 CursorPos { get; protected set; }

			public HudSpaceDelegate GetHudSpaceFunc { get; protected set; }

			public Func<Vector3D> GetNodeOriginFunc
			{
				get { return DataHandle[0].Item2[0]; }
				protected set { DataHandle[0].Item2[0] = value; }
			}

			public bool DrawCursorInHudSpace { get; set; }

			public bool IsInFront { get; protected set; }

			public bool IsFacingCamera { get; protected set; }


			public HudSpaceNodeBase(HudParentBase parent = null) : base(parent)
			{
				PlaneToWorldRef = new MatrixD[1];

				GetHudSpaceFunc = () => new MyTuple<bool, float, MatrixD>(DrawCursorInHudSpace, 1f, PlaneToWorldRef[0]);
				GetNodeOriginFunc = () => PlaneToWorldRef[0].Translation;

				_config[StateID] |= (uint)HudElementStates.IsSpaceNode;
			}


			protected override void Layout()
			{
				MatrixD camMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
				Vector3D camPos = camMatrix.Translation;
				Vector3D camForward = camMatrix.Forward;

				Vector3D nodeOrigin = PlaneToWorldRef[0].Translation;
				Vector3D nodeForward = PlaneToWorldRef[0].Forward;

				IsInFront = Vector3D.Dot(nodeOrigin - camPos, camForward) > 0;
				IsFacingCamera = IsInFront && Vector3D.Dot(nodeForward, camForward) > 0;

				MatrixD worldToPlane;
				MatrixD.Invert(ref PlaneToWorldRef[0], out worldToPlane);

				LineD cursorLine = HudMain.Cursor.WorldLine;

				PlaneD plane = new PlaneD(nodeOrigin, nodeForward);

				Vector3D worldIntersection = plane.Intersection(ref cursorLine.From, ref cursorLine.Direction);

				Vector3D localPos;
				Vector3D.TransformNoProjection(ref worldIntersection, ref worldToPlane, out localPos);


				CursorPos = new Vector3(
					(float)localPos.X,
					(float)localPos.Y,
					(float)Math.Round(Vector3D.DistanceSquared(worldIntersection, cursorLine.From), 6)
				);
			}
		}
	}
}