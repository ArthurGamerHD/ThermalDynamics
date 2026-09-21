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
			BlendTypeEnum, // blendType
			Vector2I, // bbID + matrixID
			MyStringId, // material
			MyTuple<Vector4, BoundingBox2?>, // color + mask
			MyTuple<Vector2, Vector2, Vector2>, // texCoords
			MyTuple<Vector2, Vector2, Vector2> // flat pos
		>;
		using TriangleBillboardData = MyTuple<
			BlendTypeEnum, // blendType
			Vector2I, // bbID + matrixID
			MyStringId, // material
			Vector4, // color
			MyTuple<Vector2, Vector2, Vector2>, // texCoords
			MyTuple<Vector3D, Vector3D, Vector3D> // vertexPos
		>;

		namespace Rendering
		{
			using BbUtilData = MyTuple<
				ApiMemberAccessor, // GetOrSetMember - 1
				List<MyTriangleBillboard>[], // triPoolBack - 2
				List<MyTriangleBillboard>[], // flatTriPoolBack - 3
				List<TriangleBillboardData>, // triangleList - 4
				List<FlatTriangleBillboardData>, // flatTriangleList - 5
				MyTuple<
					List<MatrixD>, // matrixBuf - 6.1
					Dictionary<MatrixD[], int>, // matrixTable - 6.2
					List<MyTriangleBillboard> // bbBuf - 6.3
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

/// <summary>BillBoardUtils operation.</summary>
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

/// <summary>Init operation.</summary>
				public static void Init()
				{
					if (instance == null)
					{
/// <summary>BillBoardUtils operation.</summary>
						instance = new BillBoardUtils();
					}
				}

/// <summary>Close operation.</summary>
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