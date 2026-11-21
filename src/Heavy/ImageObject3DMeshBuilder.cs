using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoreGMIC.Heavy
{
    public sealed class Built3DMeshData
    {
        public Mesh Mesh;
        public Vector4[] DotPositions;
        public Vector4[] DotUvs;
    }

    public static class ImageObject3DMeshBuilder
    {
        private static readonly int[][] TetCorners =
        {
            new[] { 0, 5, 1, 6 },
            new[] { 0, 1, 2, 6 },
            new[] { 0, 2, 3, 6 },
            new[] { 0, 3, 7, 6 },
            new[] { 0, 7, 4, 6 },
            new[] { 0, 4, 5, 6 }
        };

        private static readonly Vector3[] CubeCornerOffsets =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(1f, 1f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(1f, 0f, 1f),
            new Vector3(1f, 1f, 1f),
            new Vector3(0f, 1f, 1f)
        };

        public static Built3DMeshData Build(int typeIndex)
        {
            MeshAccumulator acc = new MeshAccumulator();
            bool usePlanarT3d = false;

            switch (typeIndex)
            {
                case 0:
                    BuildPlane(acc);
                    break;
                case 1:
                    BuildCube(acc);
                    break;
                case 2:
                    BuildPyramid(acc);
                    break;
                case 3:
                    BuildSphere(acc, 128, 64);
                    break;
                case 4:
                    BuildTorus(acc, 100f, 30f, 100, 50);
                    usePlanarT3d = true;
                    break;
                case 5:
                    BuildIsosurface(acc, 24, -5f, 5f, EvalGyroid, GradGyroid);
                    usePlanarT3d = true;
                    break;
                case 6:
                    BuildIsosurface(acc, 32, -4.7f, 4.7f, EvalWeird, GradWeird);
                    usePlanarT3d = true;
                    break;
                case 7:
                    BuildCup(acc, 128);
                    usePlanarT3d = true;
                    break;
                case 8:
                    BuildRubik(acc, 5, 5f, 5f);
                    break;
                default:
                    BuildCube(acc);
                    break;
            }

            return acc.Finalize(usePlanarT3d);
        }

        private static void BuildPlane(MeshAccumulator acc)
        {
            int v0 = acc.AddVertex(new Vector3(-0.5f, -0.5f, 0f), new Vector2(0f, 0f));
            int v1 = acc.AddVertex(new Vector3( 0.5f, -0.5f, 0f), new Vector2(1f, 0f));
            int v2 = acc.AddVertex(new Vector3( 0.5f,  0.5f, 0f), new Vector2(1f, 1f));
            int v3 = acc.AddVertex(new Vector3(-0.5f,  0.5f, 0f), new Vector2(0f, 1f));
            acc.AddQuad(v0, v1, v2, v3);
        }

        private static void BuildCube(MeshAccumulator acc)
        {
            int[] f0 = AddSharedVertices(acc,
                new Vector3(-0.5f, -0.5f,  0.5f), new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f), new Vector3(-0.5f,  0.5f, -0.5f));

            AddMappedQuad(acc, f0[0], f0[1], f0[2], f0[3]); // Front (+Z)
            AddMappedQuad(acc, f0[1], f0[5], f0[6], f0[2]); // Right (+X)
            AddMappedQuad(acc, f0[5], f0[4], f0[7], f0[6]); // Back (-Z)
            AddMappedQuad(acc, f0[4], f0[0], f0[3], f0[7]); // Left (-X)
            AddMappedQuad(acc, f0[3], f0[2], f0[6], f0[7]); // Top (+Y)
            AddMappedQuad(acc, f0[4], f0[5], f0[1], f0[0]); // Bottom (-Y)
        }

        private static void BuildPyramid(MeshAccumulator acc)
        {
            int v0 = acc.AddVertex(new Vector3(-0.5f, -0.5f, -0.5f), Vector2.zero);
            int v1 = acc.AddVertex(new Vector3( 0.5f, -0.5f, -0.5f), Vector2.zero);
            int v2 = acc.AddVertex(new Vector3( 0.5f,  0.5f, -0.5f), Vector2.zero);
            int v3 = acc.AddVertex(new Vector3(-0.5f,  0.5f, -0.5f), Vector2.zero);
            int v4 = acc.AddVertex(new Vector3( 0f,    0f,    0.5f), Vector2.zero);

            acc.AddQuad(v0, v3, v2, v1,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));

            Vector2 uvL = new Vector2(0f, 0f);
            Vector2 uvTop = new Vector2(0.5f, 1f);
            Vector2 uvR = new Vector2(1f, 0f);

            acc.AddTriangle(v0, v1, v4, uvL, uvR, uvTop);
            acc.AddTriangle(v1, v2, v4, uvL, uvR, uvTop);
            acc.AddTriangle(v2, v3, v4, uvL, uvR, uvTop);
            acc.AddTriangle(v3, v0, v4, uvL, uvR, uvTop);
        }

        private static void BuildSphere(MeshAccumulator acc, int segU, int segV)
        {
            int northPole = acc.AddVertex(new Vector3(0f, 0f, 1f), new Vector2(0.5f, 1f));
            int southPole = acc.AddVertex(new Vector3(0f, 0f, -1f), new Vector2(0.5f, 0f));

            int rings = segV - 2;
            int[] ringVerts = new int[rings * segU];
            for (int r = 0; r < rings; r++)
            {
                float phi = Mathf.PI * (r + 1) / (segV - 1);
                float sinP = Mathf.Sin(phi);
                float cosP = Mathf.Cos(phi);
                for (int s = 0; s < segU; s++)
                {
                    float theta = 2f * Mathf.PI * s / segU;
                    Vector3 pos = new Vector3(sinP * Mathf.Cos(theta), sinP * Mathf.Sin(theta), cosP);
                    Vector2 uv = new Vector2((float)s / segU, 1f - (float)(r + 1) / (segV - 1));
                    ringVerts[r * segU + s] = acc.AddVertex(pos, uv);
                }
            }

            for (int s = 0; s < segU; s++)
            {
                int sNext = (s + 1) % segU;
                float u0 = (float)s / segU;
                float u1 = (float)(s + 1) / segU;
                float vTop = 1f - 1f / (segV - 1);

                acc.AddTriangle(
                    northPole,
                    ringVerts[s],
                    ringVerts[sNext],
                    new Vector2(0.5f, 1f),
                    new Vector2(u0, vTop),
                    new Vector2(u1, vTop));

                for (int r = 0; r < rings - 1; r++)
                {
                    float va = 1f - (float)(r + 1) / (segV - 1);
                    float vb = 1f - (float)(r + 2) / (segV - 1);
                    int i0 = ringVerts[r * segU + s];
                    int i1 = ringVerts[(r + 1) * segU + s];
                    int i2 = ringVerts[(r + 1) * segU + sNext];
                    int i3 = ringVerts[r * segU + sNext];

                    acc.AddQuad(i0, i1, i2, i3,
                        new Vector2(u0, va), new Vector2(u0, vb), new Vector2(u1, vb), new Vector2(u1, va));
                }

                int lastRing = (rings - 1) * segU;
                float vBot = 1f - (float)rings / (segV - 1);
                acc.AddTriangle(
                    southPole,
                    ringVerts[lastRing + sNext],
                    ringVerts[lastRing + s],
                    new Vector2(0.5f, 0f),
                    new Vector2(u1, vBot),
                    new Vector2(u0, vBot));
            }
        }

        private static void BuildTorus(MeshAccumulator acc, float majorR, float minorR, int segU, int segV)
        {
            int[] verts = new int[segU * segV];
            for (int i = 0; i < segU; i++)
            {
                float u = 2f * Mathf.PI * i / segU;
                float cosU = Mathf.Cos(u);
                float sinU = Mathf.Sin(u);
                for (int j = 0; j < segV; j++)
                {
                    float v = 2f * Mathf.PI * j / segV;
                    float cosV = Mathf.Cos(v);
                    float sinV = Mathf.Sin(v);

                    float ring = majorR + minorR * cosV;
                    Vector3 pos = new Vector3(ring * cosU, ring * sinU, minorR * sinV);
                    verts[i * segV + j] = acc.AddVertex(pos, Vector2.zero);
                }
            }

            for (int i = 0; i < segU; i++)
            {
                int iNext = (i + 1) % segU;
                for (int j = 0; j < segV; j++)
                {
                    int jNext = (j + 1) % segV;
                    int p0 = verts[i * segV + j];
                    int p1 = verts[iNext * segV + j];
                    int p2 = verts[iNext * segV + jNext];
                    int p3 = verts[i * segV + jNext];
                    acc.AddQuad(p0, p1, p2, p3);
                }
            }
        }

        private static void BuildCup(MeshAccumulator acc, int radialSegs)
        {
            List<Vector2> profile = new List<Vector2>();

            // Outer bowl: from rim (40, 0) down to bowl-stem junction (8, 80) -> scaled y in [-1, 1]
            const int bowlSteps = 20;
            for (int i = 0; i <= bowlSteps; i++)
            {
                float t = (float)i / bowlSteps * (Mathf.PI * 0.44f);
                float rx = 40f * Mathf.Cos(t);
                float y = 80f * Mathf.Sin(t);
                profile.Add(new Vector2(rx, -y));
            }

            // Stem & base foot
            profile.Add(new Vector2(8f, -90f));
            profile.Add(new Vector2(20f, -180f));
            profile.Add(new Vector2(30f, -186f));
            profile.Add(new Vector2(30f, -196f));
            profile.Add(new Vector2(22f, -200f));
            profile.Add(new Vector2(0.01f, -200f));

            // Inner bowl: from rim (35, 0) down to center (0.01, -70)
            const int innerSteps = 16;
            List<Vector2> inner = new List<Vector2>();
            for (int i = 0; i <= innerSteps; i++)
            {
                float t = (float)i / innerSteps * (Mathf.PI * 0.5f);
                float rx = Mathf.Max(0.01f, 35f * Mathf.Cos(t));
                float y = 70f * Mathf.Sin(t);
                inner.Add(new Vector2(rx, -y));
            }

            // Connect outer rim -> inner rim -> inner bowl center
            List<Vector2> fullProfile = new List<Vector2>();
            for (int i = inner.Count - 1; i >= 0; i--)
            {
                fullProfile.Add(inner[i]);
            }
            for (int i = 0; i < profile.Count; i++)
            {
                fullProfile.Add(profile[i]);
            }

            int pCount = fullProfile.Count;
            int[] ringIdx = new int[radialSegs * pCount];
            for (int r = 0; r < radialSegs; r++)
            {
                float angle = 2f * Mathf.PI * r / radialSegs;
                float c = Mathf.Cos(angle);
                float s = Mathf.Sin(angle);
                for (int p = 0; p < pCount; p++)
                {
                    Vector2 pt = fullProfile[p];
                    ringIdx[r * pCount + p] = acc.AddVertex(new Vector3(pt.x * c, pt.y, pt.x * s), Vector2.zero);
                }
            }

            for (int r = 0; r < radialSegs; r++)
            {
                int rNext = (r + 1) % radialSegs;
                for (int p = 0; p < pCount - 1; p++)
                {
                    int i0 = ringIdx[r * pCount + p];
                    int i1 = ringIdx[r * pCount + (p + 1)];
                    int i2 = ringIdx[rNext * pCount + (p + 1)];
                    int i3 = ringIdx[rNext * pCount + p];
                    acc.AddQuad(i0, i1, i2, i3);
                }
            }
        }

        private static void BuildRubik(MeshAccumulator acc, int tilesPerSide, float xyShift, float zShift)
        {
            // Build one 5x5 beveled face in [0..100, 0..100], centered at [-50, 50] on Z = -50, then rotate to 6 faces.
            float tileSize = 100f / tilesPerSide;
            float sIn = xyShift * (tileSize / 100f);
            float zOut = zShift * (tileSize / 100f);

            Quaternion[] faceRotations =
            {
                Quaternion.identity,
                Quaternion.AngleAxis(-90f, Vector3.up),
                Quaternion.AngleAxis(-180f, Vector3.up),
                Quaternion.AngleAxis(-270f, Vector3.up),
                Quaternion.AngleAxis(-90f, Vector3.forward) * Quaternion.AngleAxis(-90f, Vector3.up),
                Quaternion.AngleAxis(180f, Vector3.forward) * Quaternion.AngleAxis(90f, Vector3.up)
            };

            // Simpler exact 6 cube face rotations from front (+Z or -Z face):
            faceRotations = new[]
            {
                Quaternion.identity,
                Quaternion.Euler(0f, -90f, 0f),
                Quaternion.Euler(0f, 180f, 0f),
                Quaternion.Euler(0f, 90f, 0f),
                Quaternion.Euler(90f, 0f, 0f),
                Quaternion.Euler(-90f, 0f, 0f)
            };

            for (int f = 0; f < faceRotations.Length; f++)
            {
                Matrix4x4 rot = Matrix4x4.Rotate(faceRotations[f]);
                for (int iy = 0; iy < tilesPerSide; iy++)
                {
                    for (int ix = 0; ix < tilesPerSide; ix++)
                    {
                        float x0 = ix * tileSize - 50f;
                        float y0 = iy * tileSize - 50f;
                        float x1 = x0 + tileSize;
                        float y1 = y0 + tileSize;

                        float ix0 = x0 + sIn;
                        float iy0 = y0 + sIn;
                        float ix1 = x1 - sIn;
                        float iy1 = y1 - sIn;

                        float zBase = -50f;
                        float zCap = -50f - zOut;

                        Vector3[] pts =
                        {
                            rot.MultiplyPoint3x4(new Vector3(x0,  y0,  zBase)),
                            rot.MultiplyPoint3x4(new Vector3(x1,  y0,  zBase)),
                            rot.MultiplyPoint3x4(new Vector3(x1,  y1,  zBase)),
                            rot.MultiplyPoint3x4(new Vector3(x0,  y1,  zBase)),
                            rot.MultiplyPoint3x4(new Vector3(ix0, iy0, zCap)),
                            rot.MultiplyPoint3x4(new Vector3(ix1, iy0, zCap)),
                            rot.MultiplyPoint3x4(new Vector3(ix1, iy1, zCap)),
                            rot.MultiplyPoint3x4(new Vector3(ix0, iy1, zCap))
                        };

                        Vector2[] uvs =
                        {
                            new Vector2((x0 + 50f) * 0.01f,  (y0 + 50f) * 0.01f),
                            new Vector2((x1 + 50f) * 0.01f,  (y0 + 50f) * 0.01f),
                            new Vector2((x1 + 50f) * 0.01f,  (y1 + 50f) * 0.01f),
                            new Vector2((x0 + 50f) * 0.01f,  (y1 + 50f) * 0.01f),
                            new Vector2((ix0 + 50f) * 0.01f, (iy0 + 50f) * 0.01f),
                            new Vector2((ix1 + 50f) * 0.01f, (iy0 + 50f) * 0.01f),
                            new Vector2((ix1 + 50f) * 0.01f, (iy1 + 50f) * 0.01f),
                            new Vector2((ix0 + 50f) * 0.01f, (iy1 + 50f) * 0.01f)
                        };

                        int[] idx = new int[8];
                        for (int k = 0; k < 8; k++)
                        {
                            idx[k] = acc.AddVertex(pts[k], uvs[k]);
                        }

                        // Front bevel cap + 4 angled bevel walls
                        acc.AddQuad(idx[4], idx[7], idx[6], idx[5], uvs[4], uvs[7], uvs[6], uvs[5]);
                        acc.AddQuad(idx[0], idx[4], idx[5], idx[1], uvs[0], uvs[4], uvs[5], uvs[1]);
                        acc.AddQuad(idx[3], idx[2], idx[6], idx[7], uvs[3], uvs[2], uvs[6], uvs[7]);
                        acc.AddQuad(idx[0], idx[3], idx[7], idx[4], uvs[0], uvs[3], uvs[7], uvs[4]);
                        acc.AddQuad(idx[1], idx[5], idx[6], idx[2], uvs[1], uvs[5], uvs[6], uvs[2]);
                    }
                }
            }
        }

        private static void BuildIsosurface(
            MeshAccumulator acc,
            int gridRes,
            float minCoord,
            float maxCoord,
            Func<float, float, float, float> evalFn,
            Func<float, float, float, Vector3> gradFn)
        {
            int n = gridRes;
            int n2 = n * n;
            float[] vals = new float[n * n * n];
            float step = (maxCoord - minCoord) / Mathf.Max(1, n - 1);

            for (int z = 0; z < n; z++)
            {
                float fz = minCoord + z * step;
                for (int y = 0; y < n; y++)
                {
                    float fy = minCoord + y * step;
                    int rowOffset = z * n2 + y * n;
                    for (int x = 0; x < n; x++)
                    {
                        float fx = minCoord + x * step;
                        vals[rowOffset + x] = evalFn(fx, fy, fz);
                    }
                }
            }

            Dictionary<long, int> edgeVertCache = new Dictionary<long, int>();
            int[] cornerGridIdx = new int[8];
            Vector3[] cornerPos = new Vector3[8];
            float[] cornerVal = new float[8];

            int[] dx = { 0, 1, 1, 0, 0, 1, 1, 0 };
            int[] dy = { 0, 0, 1, 1, 0, 0, 1, 1 };
            int[] dz = { 0, 0, 0, 0, 1, 1, 1, 1 };

            for (int z = 0; z < n - 1; z++)
            {
                for (int y = 0; y < n - 1; y++)
                {
                    for (int x = 0; x < n - 1; x++)
                    {
                        for (int c = 0; c < 8; c++)
                        {
                            int cx = x + dx[c];
                            int cy = y + dy[c];
                            int cz = z + dz[c];
                            int gIdx = cz * n2 + cy * n + cx;
                            cornerGridIdx[c] = gIdx;
                            cornerVal[c] = vals[gIdx];
                            cornerPos[c] = new Vector3(
                                minCoord + cx * step,
                                minCoord + cy * step,
                                minCoord + cz * step);
                        }

                        for (int t = 0; t < 6; t++)
                        {
                            int[] tc = TetCorners[t];
                            int mask = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                if (cornerVal[tc[i]] >= 0f)
                                {
                                    mask |= (1 << i);
                                }
                            }

                            if (mask == 0 || mask == 15)
                            {
                                continue;
                            }

                            int[] inside = new int[4];
                            int[] outside = new int[4];
                            int inCnt = 0;
                            int outCnt = 0;

                            for (int i = 0; i < 4; i++)
                            {
                                if ((mask & (1 << i)) != 0)
                                {
                                    inside[inCnt++] = tc[i];
                                }
                                else
                                {
                                    outside[outCnt++] = tc[i];
                                }
                            }

                            if (inCnt == 1)
                            {
                                int i0 = GetEdgeVertex(acc, edgeVertCache, cornerGridIdx, cornerPos, cornerVal, inside[0], outside[0], gradFn);
                                int i1 = GetEdgeVertex(acc, edgeVertCache, cornerGridIdx, cornerPos, cornerVal, inside[0], outside[1], gradFn);
                                int i2 = GetEdgeVertex(acc, edgeVertCache, cornerGridIdx, cornerPos, cornerVal, inside[0], outside[2], gradFn);
                                acc.AddOrientedTriangle(i0, i1, i2);
                            }
                            else if (inCnt == 3)
                            {
                                int i0 = GetEdgeVertex(acc, edgeVertCache, cornerGridIdx, cornerPos, cornerVal, outside[0], inside[0], gradFn);
                                int i1 = GetEdgeVertex(acc, edgeVertCache, cornerGridIdx, cornerPos, cornerVal, outside[0], inside[1], gradFn);
                                int i2 = GetEdgeVertex(acc, edgeVertCache, cornerGridIdx, cornerPos, cornerVal, outside[0], inside[2], gradFn);
                                acc.AddOrientedTriangle(i0, i1, i2);
                            }
                            else
                            {
                                // 2 inside, 2 outside -> 1 quad split into 2 triangles
                                int q0 = GetEdgeVertex(acc, edgeVertCache, cornerGridIdx, cornerPos, cornerVal, inside[0], outside[0], gradFn);
                                int q1 = GetEdgeVertex(acc, edgeVertCache, cornerGridIdx, cornerPos, cornerVal, inside[0], outside[1], gradFn);
                                int q2 = GetEdgeVertex(acc, edgeVertCache, cornerGridIdx, cornerPos, cornerVal, inside[1], outside[1], gradFn);
                                int q3 = GetEdgeVertex(acc, edgeVertCache, cornerGridIdx, cornerPos, cornerVal, inside[1], outside[0], gradFn);
                                acc.AddOrientedQuad(q0, q1, q2, q3);
                            }
                        }
                    }
                }
            }
        }

        private static int GetEdgeVertex(
            MeshAccumulator acc,
            Dictionary<long, int> cache,
            int[] cornerGridIdx,
            Vector3[] cornerPos,
            float[] cornerVal,
            int cA,
            int cB,
            Func<float, float, float, Vector3> gradFn)
        {
            int gA = cornerGridIdx[cA];
            int gB = cornerGridIdx[cB];
            long key = (gA < gB) ? (((long)gA << 32) | (uint)gB) : (((long)gB << 32) | (uint)gA);

            if (cache.TryGetValue(key, out int existing))
            {
                return existing;
            }

            float vA = cornerVal[cA];
            float vB = cornerVal[cB];
            float denom = vA - vB;
            float t = (Mathf.Abs(denom) > 1e-6f) ? Mathf.Clamp01(vA / denom) : 0.5f;

            Vector3 p = Vector3.Lerp(cornerPos[cA], cornerPos[cB], t);
            Vector3 n = gradFn(p.x, p.y, p.z).normalized;
            if (n.sqrMagnitude < 1e-6f)
            {
                n = Vector3.forward;
            }

            int idx = acc.AddVertex(p, Vector2.zero, n);
            cache[key] = idx;
            return idx;
        }

        private static float EvalGyroid(float x, float y, float z)
        {
            float pi = Mathf.PI;
            float p1 =
                Mathf.Cos( 2f * x + y + z - pi) + Mathf.Cos( 2f * x - y + z - pi) +
                Mathf.Cos(-2f * x + y - z - pi) + Mathf.Cos(-2f * x - y - z - pi) +
                Mathf.Cos( x + 2f * y + z - pi) + Mathf.Cos( x + 2f * y - z - pi) +
                Mathf.Cos(-x - 2f * y + z - pi) + Mathf.Cos(-x - 2f * y - z - pi) +
                Mathf.Cos( x + y + 2f * z - pi) + Mathf.Cos(-x + y + 2f * z - pi) +
                Mathf.Cos( x - y - 2f * z - pi) + Mathf.Cos(-x - y - 2f * z - pi) +
                Mathf.Cos(-2f * x + y + z)      + Mathf.Cos( 2f * x + y - z)      +
                Mathf.Cos(-2f * x - y + z)      + Mathf.Cos( 2f * x - y - z)      +
                Mathf.Cos(-x + 2f * y + z)      + Mathf.Cos( x - 2f * y + z)      +
                Mathf.Cos(-x + 2f * y - z)      + Mathf.Cos( x - 2f * y - z)      +
                Mathf.Cos( x - y + 2f * z)      + Mathf.Cos( x + y - 2f * z)      +
                Mathf.Cos(-x - y + 2f * z)      + Mathf.Cos(-x + y - 2f * z);

            float p2 =
                Mathf.Cos(-2f * x + 2f * y - pi) + Mathf.Cos( 2f * x - 2f * y - pi) +
                Mathf.Cos( 2f * x + 2f * y - pi) + Mathf.Cos(-2f * x - 2f * y - pi) +
                Mathf.Cos(-2f * y + 2f * z - pi) + Mathf.Cos( 2f * y - 2f * z - pi) +
                Mathf.Cos( 2f * y + 2f * z - pi) + Mathf.Cos(-2f * y - 2f * z - pi) +
                Mathf.Cos(-2f * z + 2f * x - pi) + Mathf.Cos( 2f * z - 2f * x - pi) +
                Mathf.Cos( 2f * z + 2f * x - pi) + Mathf.Cos(-2f * z - 2f * x - pi);

            return 0.49f * p1 + 0.27f * p2 - 0.69f;
        }

        private static Vector3 GradGyroid(float x, float y, float z)
        {
            const float eps = 0.01f;
            float gx = EvalGyroid(x + eps, y, z) - EvalGyroid(x - eps, y, z);
            float gy = EvalGyroid(x, y + eps, z) - EvalGyroid(x, y - eps, z);
            float gz = EvalGyroid(x, y, z + eps) - EvalGyroid(x, y, z - eps);
            return -new Vector3(gx, gy, gz);
        }

        private static float EvalWeird(float x, float y, float z)
        {
            const float t = 1.61803399f;
            return 2f - (
                Mathf.Cos(x + t * y) + Mathf.Cos(x - t * y) +
                Mathf.Cos(y + t * z) + Mathf.Cos(y - t * z) +
                Mathf.Cos(z - t * x) + Mathf.Cos(z + t * x));
        }

        private static Vector3 GradWeird(float x, float y, float z)
        {
            const float t = 1.61803399f;
            float gx = (Mathf.Sin(x + t * y) + Mathf.Sin(x - t * y) - t * Mathf.Sin(z - t * x) + t * Mathf.Sin(z + t * x));
            float gy = (t * Mathf.Sin(x + t * y) - t * Mathf.Sin(x - t * y) + Mathf.Sin(y + t * z) + Mathf.Sin(y - t * z));
            float gz = (t * Mathf.Sin(y + t * z) - t * Mathf.Sin(y - t * z) + Mathf.Sin(z - t * x) + Mathf.Sin(z + t * x));
            return -new Vector3(gx, gy, gz);
        }

        private static int[] AddSharedVertices(MeshAccumulator acc, params Vector3[] positions)
        {
            int[] idx = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                idx[i] = acc.AddVertex(positions[i], Vector2.zero);
            }
            return idx;
        }

        private static void AddMappedQuad(MeshAccumulator acc, int v0, int v1, int v2, int v3)
        {
            acc.AddQuad(v0, v1, v2, v3,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        }

        private sealed class MeshAccumulator
        {
            private readonly List<Vector3> _positions = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<Vector3> _explicitNormals = new List<Vector3>();
            private bool _hasExplicitNormals;

            private readonly List<TriEntry> _tris = new List<TriEntry>();

            private readonly struct TriEntry
            {
                public readonly int I0, I1, I2;
                public readonly Vector2 Uv0, Uv1, Uv2;
                public readonly Vector3 EdgeMask;

                public TriEntry(int i0, int i1, int i2, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector3 edgeMask)
                {
                    I0 = i0; I1 = i1; I2 = i2;
                    Uv0 = uv0; Uv1 = uv1; Uv2 = uv2;
                    EdgeMask = edgeMask;
                }
            }

            public int AddVertex(Vector3 pos, Vector2 uv)
            {
                int idx = _positions.Count;
                _positions.Add(pos);
                _uvs.Add(uv);
                _explicitNormals.Add(Vector3.zero);
                return idx;
            }

            public int AddVertex(Vector3 pos, Vector2 uv, Vector3 explicitNormal)
            {
                _hasExplicitNormals = true;
                int idx = _positions.Count;
                _positions.Add(pos);
                _uvs.Add(uv);
                _explicitNormals.Add(explicitNormal);
                return idx;
            }

            public void AddTriangle(int i0, int i1, int i2)
            {
                _tris.Add(new TriEntry(i0, i1, i2, _uvs[i0], _uvs[i1], _uvs[i2], Vector3.one));
            }

            public void AddTriangle(int i0, int i1, int i2, Vector2 uv0, Vector2 uv1, Vector2 uv2)
            {
                _tris.Add(new TriEntry(i0, i1, i2, uv0, uv1, uv2, Vector3.one));
            }

            public void AddOrientedTriangle(int i0, int i1, int i2)
            {
                Vector3 p0 = _positions[i0];
                Vector3 p1 = _positions[i1];
                Vector3 p2 = _positions[i2];
                Vector3 faceN = Vector3.Cross(p1 - p0, p2 - p0);
                Vector3 avgN = _explicitNormals[i0] + _explicitNormals[i1] + _explicitNormals[i2];
                if (Vector3.Dot(faceN, avgN) < 0f)
                {
                    int tmp = i1; i1 = i2; i2 = tmp;
                }
                AddTriangle(i0, i1, i2);
            }

            public void AddQuad(int i0, int i1, int i2, int i3)
            {
                AddQuad(i0, i1, i2, i3, _uvs[i0], _uvs[i1], _uvs[i2], _uvs[i3]);
            }

            public void AddQuad(int i0, int i1, int i2, int i3, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
            {
                // For quad (i0, i1, i2, i3), diagonal (i0, i2) is opposite vertex i1 in tri1 (mask.y=0)
                // and opposite vertex i3 in tri2 (mask.y=0).
                _tris.Add(new TriEntry(i0, i1, i2, uv0, uv1, uv2, new Vector3(1f, 0f, 1f)));
                _tris.Add(new TriEntry(i0, i2, i3, uv0, uv2, uv3, new Vector3(1f, 0f, 1f)));
            }

            public void AddOrientedQuad(int i0, int i1, int i2, int i3)
            {
                Vector3 p0 = _positions[i0];
                Vector3 p1 = _positions[i1];
                Vector3 p2 = _positions[i2];
                Vector3 faceN = Vector3.Cross(p1 - p0, p2 - p0);
                Vector3 avgN = _explicitNormals[i0] + _explicitNormals[i1] + _explicitNormals[i2] + _explicitNormals[i3];
                if (Vector3.Dot(faceN, avgN) < 0f)
                {
                    AddQuad(i0, i3, i2, i1);
                }
                else
                {
                    AddQuad(i0, i1, i2, i3);
                }
            }

            public Built3DMeshData Finalize(bool usePlanarT3d)
            {
                int vCount = _positions.Count;
                if (vCount == 0 || _tris.Count == 0)
                {
                    return new Built3DMeshData
                    {
                        Mesh = new Mesh(),
                        DotPositions = new[] { Vector4.zero },
                        DotUvs = new[] { Vector4.zero }
                    };
                }

                // c3d + n3d (center at origin, scale max dimension to 1.0)
                Vector3 minP = _positions[0];
                Vector3 maxP = _positions[0];
                for (int i = 1; i < vCount; i++)
                {
                    minP = Vector3.Min(minP, _positions[i]);
                    maxP = Vector3.Max(maxP, _positions[i]);
                }

                Vector3 center = 0.5f * (minP + maxP);
                Vector3 size = maxP - minP;
                float maxDim = Mathf.Max(1e-5f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
                float invScale = 1f / maxDim;

                for (int i = 0; i < vCount; i++)
                {
                    _positions[i] = (_positions[i] - center) * invScale;
                }

                if (usePlanarT3d)
                {
                    Vector3 minN = _positions[0];
                    Vector3 maxN = _positions[0];
                    for (int i = 1; i < vCount; i++)
                    {
                        minN = Vector3.Min(minN, _positions[i]);
                        maxN = Vector3.Max(maxN, _positions[i]);
                    }

                    float spanX = Mathf.Max(1e-5f, maxN.x - minN.x);
                    float spanY = Mathf.Max(1e-5f, maxN.y - minN.y);
                    for (int i = 0; i < vCount; i++)
                    {
                        _uvs[i] = new Vector2(
                            (_positions[i].x - minN.x) / spanX,
                            (_positions[i].y - minN.y) / spanY);
                    }
                }

                // Compute smooth vertex normals when not explicitly supplied by analytical gradient.
                Vector3[] smoothNormals = new Vector3[vCount];
                if (_hasExplicitNormals)
                {
                    for (int i = 0; i < vCount; i++)
                    {
                        Vector3 n = _explicitNormals[i];
                        smoothNormals[i] = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.forward;
                    }
                }
                else
                {
                    for (int i = 0; i < _tris.Count; i++)
                    {
                        TriEntry tri = _tris[i];
                        Vector3 p0 = _positions[tri.I0];
                        Vector3 p1 = _positions[tri.I1];
                        Vector3 p2 = _positions[tri.I2];
                        Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0);
                        smoothNormals[tri.I0] += fn;
                        smoothNormals[tri.I1] += fn;
                        smoothNormals[tri.I2] += fn;
                    }

                    for (int i = 0; i < vCount; i++)
                    {
                        smoothNormals[i] = smoothNormals[i].sqrMagnitude > 1e-6f
                            ? smoothNormals[i].normalized
                            : Vector3.forward;
                    }
                }

                // Build split per-triangle mesh so barycentric wireframe & exact face UVs coexist cleanly.
                int triVertCount = _tris.Count * 3;
                Vector3[] outVerts = new Vector3[triVertCount];
                Vector3[] outNormals = new Vector3[triVertCount];
                Vector2[] outUv0 = new Vector2[triVertCount];
                Vector3[] outBary = new Vector3[triVertCount];
                Vector3[] outEdgeMask = new Vector3[triVertCount];
                int[] outIndices = new int[triVertCount];

                Vector3 bary0 = new Vector3(1f, 0f, 0f);
                Vector3 bary1 = new Vector3(0f, 1f, 0f);
                Vector3 bary2 = new Vector3(0f, 0f, 1f);

                for (int t = 0; t < _tris.Count; t++)
                {
                    TriEntry tri = _tris[t];
                    int b = t * 3;

                    outVerts[b] = _positions[tri.I0];
                    outVerts[b + 1] = _positions[tri.I1];
                    outVerts[b + 2] = _positions[tri.I2];

                    outNormals[b] = smoothNormals[tri.I0];
                    outNormals[b + 1] = smoothNormals[tri.I1];
                    outNormals[b + 2] = smoothNormals[tri.I2];

                    outUv0[b] = usePlanarT3d ? _uvs[tri.I0] : tri.Uv0;
                    outUv0[b + 1] = usePlanarT3d ? _uvs[tri.I1] : tri.Uv1;
                    outUv0[b + 2] = usePlanarT3d ? _uvs[tri.I2] : tri.Uv2;

                    outBary[b] = bary0;
                    outBary[b + 1] = bary1;
                    outBary[b + 2] = bary2;

                    outEdgeMask[b] = tri.EdgeMask;
                    outEdgeMask[b + 1] = tri.EdgeMask;
                    outEdgeMask[b + 2] = tri.EdgeMask;

                    outIndices[b] = b;
                    outIndices[b + 1] = b + 1;
                    outIndices[b + 2] = b + 2;
                }

                Mesh mesh = new Mesh
                {
                    indexFormat = IndexFormat.UInt32
                };
                mesh.vertices = outVerts;
                mesh.normals = outNormals;
                mesh.uv = outUv0;
                mesh.SetUVs(1, outBary);
                mesh.SetUVs(2, outEdgeMask);
                mesh.SetTriangles(outIndices, 0, true);

                // Build unique dot list for Dots mode.
                Vector4[] dotPos = new Vector4[vCount];
                Vector4[] dotUvs = new Vector4[vCount];
                for (int i = 0; i < vCount; i++)
                {
                    Vector3 p = _positions[i];
                    dotPos[i] = new Vector4(p.x, p.y, p.z, 1f);
                    dotUvs[i] = new Vector4(_uvs[i].x, _uvs[i].y, 0f, 0f);
                }

                return new Built3DMeshData
                {
                    Mesh = mesh,
                    DotPositions = dotPos,
                    DotUvs = dotUvs
                };
            }
        }
    }
}