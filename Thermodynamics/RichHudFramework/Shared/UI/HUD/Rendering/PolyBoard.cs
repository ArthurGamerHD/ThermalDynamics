using System;
using System.Collections.Generic;
using VRageMath;

namespace RichHudFramework.UI.Rendering
{
	public class PolyBoard
	{
		public virtual Color Color
		{
			get { return _color; }
			set
			{
				if (value != _color)
					polyMat.bbColor = value.GetBbColor();

				_color = value;
			}
		}

		public virtual Material Material
		{
			get { return matFrame.Material; }
			set
			{
				if (value != matFrame.Material)
				{
					updateMatFit = true;
					matFrame.Material = value;
					polyMat.textureID = value.TextureID;
				}
			}
		}

		public MaterialAlignment MatAlignment
		{
			get { return matFrame.Alignment; }
			set
			{
				if (value != matFrame.Alignment)
				{
					updateMatFit = true;
					matFrame.Alignment = value;
				}
			}
		}

		public virtual int Sides
		{
			get { return _sides; }
			set
			{
				if (value != _sides)
					updateVertices = true;

				_sides = value;
			}
		}

		protected int _sides;

		protected Color _color;

		protected bool updateVertices, updateMatFit;

		protected PolyMaterial polyMat;

		protected readonly MaterialFrame matFrame;

		protected readonly List<int> triangles;
		protected readonly List<Vector2> vertices;

		protected readonly List<Vector2> drawVertices;

/// <summary>PolyBoard operation.</summary>
		public PolyBoard()
		{
/// <summary>List operation.</summary>
			triangles = new List<int>();
/// <summary>List operation.</summary>
			vertices = new List<Vector2>();
/// <summary>List operation.</summary>
			drawVertices = new List<Vector2>();

/// <summary>MaterialFrame operation.</summary>
			matFrame = new MaterialFrame();
			polyMat = PolyMaterial.Default;
/// <summary>List operation.</summary>
			polyMat.texCoords = new List<Vector2>();

			_sides = 16;
			updateVertices = true;
		}

/// <summary>Draw operation.</summary>
		public virtual void Draw(Vector2 size, Vector2 origin, MatrixD[] matrixRef)
		{
			if (_sides > 2)
			{
				if (updateVertices)
					GeneratePolygon();
			}

			if (_sides > 2 && drawVertices.Count > 2)
			{
				if (updateMatFit)
				{
					polyMat.texBounds = matFrame.GetMaterialAlignment(size.X / size.Y);
					GenerateTextureCoordinates();
					updateMatFit = false;
				}

				for (int i = 0; i < drawVertices.Count; i++)
				{
					drawVertices[i] = origin + size * vertices[i];
				}

				BillBoardUtils.AddTriangles(triangles, drawVertices, ref polyMat, matrixRef);
			}
		}

/// <summary>Draw operation.</summary>
		public virtual void Draw(Vector2 size, Vector2 origin, Vector2I faceRange, MatrixD[] matrixRef)
		{
			if (_sides > 2)
			{
				if (updateVertices)
					GeneratePolygon();
			}

			if (_sides > 2 && drawVertices.Count > 2)
			{
				if (updateMatFit)
				{
					polyMat.texBounds = matFrame.GetMaterialAlignment(size.X / size.Y);
					GenerateTextureCoordinates();
					updateMatFit = false;
				}

				int max = drawVertices.Count - 1;
				drawVertices[max] = origin + size * vertices[max];

				for (int i = 0; i < drawVertices.Count; i++)
				{
					drawVertices[i] = origin + size * vertices[i];
				}

				faceRange *= 3;
				BillBoardUtils.AddTriangleRange(faceRange, triangles, drawVertices, ref polyMat, matrixRef);
			}
		}

/// <summary>Returns the sliceoffset.</summary>
		public virtual Vector2 GetSliceOffset(Vector2 bbSize, Vector2I range)
		{
			if (updateVertices)
				GeneratePolygon();

			int max = vertices.Count;
			Vector2 start = vertices[range.X],
				end = vertices[(range.Y + 1) % max],
				center = Vector2.Zero;

			return bbSize * (start + end + center) / 3f;
		}

/// <summary>GeneratePolygon operation.</summary>
		protected virtual void GeneratePolygon()
		{
			GenerateVertices();
			GenerateTriangles();
			drawVertices.Clear();

			for (int i = 0; i < vertices.Count; i++)
				drawVertices.Add(Vector2.Zero);

			updateMatFit = true;
		}

/// <summary>GenerateTriangles operation.</summary>
		protected virtual void GenerateTriangles()
		{
			int max = vertices.Count - 1;
			triangles.Clear();
			triangles.EnsureCapacity(_sides * 3);

			for (int i = 0; i < vertices.Count - 1; i++)
			{
				triangles.Add(max);
				triangles.Add(i);
				triangles.Add((i + 1) % max);
			}
		}

/// <summary>GenerateTextureCoordinates operation.</summary>
		protected virtual void GenerateTextureCoordinates()
		{
			Vector2 texScale = polyMat.texBounds.Size,
				texCenter = polyMat.texBounds.Center;

			polyMat.texCoords.Clear();
			polyMat.texCoords.EnsureCapacity(vertices.Count);

			for (int i = 0; i < vertices.Count; i++)
			{
				Vector2 uv = vertices[i] * texScale;
				uv.Y *= -1f;

				polyMat.texCoords.Add(uv + texCenter);
			}
		}

/// <summary>GenerateVertices operation.</summary>
		protected virtual void GenerateVertices()
		{
			float rotStep = (float)(Math.PI * 2f / _sides),
				rotPos = -.5f * rotStep;

			vertices.Clear();
			vertices.EnsureCapacity(_sides + 1);

			for (int i = 0; i < _sides; i++)
			{
				Vector2 point = Vector2.Zero;
				point.X = (float)Math.Cos(rotPos);
				point.Y = (float)Math.Sin(rotPos);

				vertices.Add(.5f * point);
				rotPos += rotStep;
			}

			vertices.Add(Vector2.Zero);
		}
	}
}