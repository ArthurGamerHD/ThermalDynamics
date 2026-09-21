using VRageMath;
using System.Collections.Generic;

namespace RichHudFramework
{
    namespace UI
    {
        namespace Rendering
        {
            public class BlockBoard
            {
                public Vector3D Size { get; set; }

                public Vector3D Offset { get; set; }

                public MatBoard Front => faces[0];

                public MatBoard Back => faces[1];

                public MatBoard Top => faces[2];

                public MatBoard Bottom => faces[3];

                public MatBoard Left => faces[4];

                public MatBoard Right => faces[5];

                public IReadOnlyList<MatBoard> Faces => faces;

                protected readonly MatBoard[] faces;

                protected readonly Vector3D[] octant;

/// <summary>BlockBoard operation.</summary>
                public BlockBoard()
                {
                    faces = new MatBoard[6];
                    octant = new Vector3D[8];

                    for (int n = 0; n < 6; n++)
/// <summary>MatBoard operation.</summary>
                        faces[n] = new MatBoard();
                }

/// <summary>Sets the color.</summary>
                public void SetColor(Color color)
                {
                    for (int n = 0; n < 6; n++)
                        faces[n].Color = color;
                }

/// <summary>Sets the material.</summary>
                public void SetMaterial(Material material)
                {
                    for (int n = 0; n < 6; n++)
                        faces[n].Material = material;
                }

/// <summary>Sets the materialalignment.</summary>
                public void SetMaterialAlignment(MaterialAlignment materialAlignment)
                {
                    for (int n = 0; n < 6; n++)
                        faces[n].MatAlignment = materialAlignment;
                }

/// <summary>Draw operation.</summary>
                public void Draw(ref MatrixD matrix)
                {
                    MyQuadD faceQuad;
                    UpdateOctant(ref matrix);

                    faceQuad.Point0 = octant[3];
                    faceQuad.Point1 = octant[2];
                    faceQuad.Point2 = octant[1];
                    faceQuad.Point3 = octant[0];

                    faces[0].Draw(ref faceQuad);

                    faceQuad.Point0 = octant[4];
                    faceQuad.Point1 = octant[5];
                    faceQuad.Point2 = octant[6];
                    faceQuad.Point3 = octant[7];

                    faces[1].Draw(ref faceQuad);

                    faceQuad.Point0 = octant[7];
                    faceQuad.Point1 = octant[6];
                    faceQuad.Point2 = octant[2];
                    faceQuad.Point3 = octant[3];

                    faces[2].Draw(ref faceQuad);

                    faceQuad.Point0 = octant[0];
                    faceQuad.Point1 = octant[1];
                    faceQuad.Point2 = octant[5];
                    faceQuad.Point3 = octant[4];

                    faces[3].Draw(ref faceQuad);

                    faceQuad.Point0 = octant[0];
                    faceQuad.Point1 = octant[4];
                    faceQuad.Point2 = octant[7];
                    faceQuad.Point3 = octant[3];

                    faces[4].Draw(ref faceQuad);

                    faceQuad.Point0 = octant[5];
                    faceQuad.Point1 = octant[1];
                    faceQuad.Point2 = octant[2];
                    faceQuad.Point3 = octant[6];

                    faces[5].Draw(ref faceQuad);
                }

/// <summary>UpdateOctant operation.</summary>
                private void UpdateOctant(ref MatrixD matrix)
                {
                    Vector3D size = Size * 0.5d;

/// <summary>Vector3D operation.</summary>
                    octant[0] = new Vector3D(-size.X, size.Y, -size.Z);
/// <summary>Vector3D operation.</summary>
                    octant[1] = new Vector3D(size.X, size.Y, -size.Z);
/// <summary>Vector3D operation.</summary>
                    octant[2] = new Vector3D(size.X, -size.Y, -size.Z);
/// <summary>Vector3D operation.</summary>
                    octant[3] = new Vector3D(-size.X, -size.Y, -size.Z);

/// <summary>Vector3D operation.</summary>
                    octant[4] = new Vector3D(-size.X, size.Y, size.Z);
/// <summary>Vector3D operation.</summary>
                    octant[5] = new Vector3D(size.X, size.Y, size.Z);
/// <summary>Vector3D operation.</summary>
                    octant[6] = new Vector3D(size.X, -size.Y, size.Z);
/// <summary>Vector3D operation.</summary>
                    octant[7] = new Vector3D(-size.X, -size.Y, size.Z);

                    for (int n = 0; n < 8; n++)
                        octant[n] = Vector3D.Transform(octant[n], ref matrix) + Offset;
                }
            }
        }
    }
}