using RichHudFramework.Client;
using RichHudFramework.Internal;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Utils;
using VRageMath;
using VRageRender;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace RichHudFramework
{
	namespace UI
	{
		using ApiMemberAccessor = System.Func<object, int, object>;
		using FlatTriangleBillboardData = MyTuple<
			BlendTypeEnum,
			Vector2I,
			MyStringId,
			MyTuple<Vector4, BoundingBox2?>,
			MyTuple<Vector2, Vector2, Vector2>,
			MyTuple<Vector2, Vector2, Vector2>
		>;
		using TriangleBillboardData = MyTuple<
			BlendTypeEnum,
			Vector2I,
			MyStringId,
			Vector4,
			MyTuple<Vector2, Vector2, Vector2>,
			MyTuple<Vector3D, Vector3D, Vector3D>
		>;

		namespace Rendering
		{
			using BbUtilData = MyTuple<
				ApiMemberAccessor,
				List<MyTriangleBillboard>[],
				List<MyTriangleBillboard>[],
				List<TriangleBillboardData>,
				List<FlatTriangleBillboardData>,
				MyTuple<
					List<MatrixD>,
					Dictionary<MatrixD[], int>,
					List<MyTriangleBillboard>
				>
			>;

			public sealed partial class BillBoardUtils : RichHudClient.ApiModule
			{
				private static BillBoardUtils instance;

				private readonly List<MyTriangleBillboard>[] triPoolBack;
				private readonly List<MyTriangleBillboard>[] flatTriPoolBack;
				private readonly List<MyTriangleBillboard> bbBuf;

				private readonly List<TriangleBillboardData> triangleList;
				private readonly List<FlatTriangleBillboardData> flatTriangleList;
				private readonly List<MatrixD> matrixBuf;
				private readonly Dictionary<MatrixD[], int> matrixTable;

				private readonly ApiMemberAccessor GetOrSetMember;


				private BillBoardUtils() : base(ApiModuleTypes.BillBoardUtils, false, true)
				{
					if (instance != null)
						throw new Exception($"Only one instance of {GetType().Name} can exist at once.");

					var data = (IReadOnlyList<BbUtilData>)GetApiData();
					GetOrSetMember = data[0].Item1;
					triPoolBack = data[0].Item2;
					flatTriPoolBack = data[0].Item3;
					triangleList = data[0].Item4;
					flatTriangleList = data[0].Item5;
					matrixBuf = data[0].Item6.Item1;
					matrixTable = data[0].Item6.Item2;
					bbBuf = data[0].Item6.Item3;
				}


				public static void Init()
				{
					if (instance == null)
					{

						instance = new BillBoardUtils();
					}
				}


				public override void Close()
				{
					if (ExceptionHandler.Unloading)
					{
						instance = null;
					}
				}
			}
		}
	}
}