using System;
using UnityEngine;

namespace ToyTricksDemo;

/// <summary>
/// The replicated-shear factorization, ported verbatim from the ProjectMER fork's
/// TrianglePrimitiveBuilder (.references\Reference Plugins\ProjectMER\Features\TrianglePrimitiveBuilder.cs).
///
/// WHY THIS WORKS: a single AdminToy only replicates Position + Rotation(quaternion) + Scale(Vector3),
/// i.e. R*S — a rotated box, never a shear. But AdminToyBase serializes the PARENT netId and the client
/// rebuilds the real Unity hierarchy with LOCAL transforms (AdminToys.AdminToyBase: OnSerialize ->
/// ServerParentId, OnStartClient -> UpdateParent). So a NON-UNIFORM-scaled invisible parent composed with
/// a rotated child yields a genuinely non-orthogonal localToWorldMatrix on every client = real shear.
///
/// Any parallelogram spanned by edge vectors (u, v) factors by 2x2 SVD into
///   A = [u|v] = U * Sigma * V^T
///   parent: rotation U (in the parallelogram's plane basis), scale (sigmaX, sigmaY, 1)
///   child:  rotation -V^T about the plane normal, unit scale (give the child z-scale for thickness).
/// In-game verified 2026-06-29 (ProjectMER Triangle blocks / the QRT daisy emblem).
///
/// TryGetShearRig3D extends this to a full 3x3 SVD: three arbitrary edge vectors span a parallelepiped
/// = a cube skewed on EVERY axis at once, still just parent (U, Sigma) + child (V^T) = 2 toys.
/// </summary>
public static class ShearMath
{
    /// <summary>
    /// Factors the parallelogram spanned by <paramref name="edgeX"/> and <paramref name="edgeY"/>
    /// (origin at a corner) into a replicable parent/child shear rig.
    /// <paramref name="center"/> is the offset from the origin corner to the parallelogram's center —
    /// place the PARENT at origin + center. Returns false for degenerate (collinear/zero) edges.
    /// </summary>
    public static bool TryGetShearRig(
        Vector3 edgeX,
        Vector3 edgeY,
        out Vector3 center,
        out Quaternion parentRotation,
        out Vector3 parentScale,
        out Quaternion childRotation)
    {
        center = (edgeX + edgeY) * 0.5f;
        parentRotation = Quaternion.identity;
        parentScale = Vector3.one;
        childRotation = Quaternion.identity;

        if (edgeX.sqrMagnitude < 0.000001f || edgeY.sqrMagnitude < 0.000001f)
        {
            return false;
        }

        Vector3 normal = Vector3.Cross(edgeX, edgeY);
        if (normal.sqrMagnitude < 0.000001f)
        {
            return false;
        }

        normal.Normalize();
        Vector3 basisX = edgeX.normalized;
        Vector3 basisY = Vector3.Cross(normal, basisX).normalized;

        // The parallelogram matrix expressed in its own plane basis (upper-triangular 2x2).
        float m00 = edgeX.magnitude;
        float m01 = Vector3.Dot(edgeY, basisX);
        float m11 = Vector3.Dot(edgeY, basisY);

        GetSvd2x2(m00, m01, 0f, m11, out float uAngle, out float singularX, out float singularY, out float vAngle);

        if (singularX < 0.000001f || singularY < 0.000001f)
        {
            return false;
        }

        Quaternion basisRotation = Quaternion.LookRotation(normal, basisY);
        parentRotation = basisRotation * Quaternion.AngleAxis(uAngle * Mathf.Rad2Deg, Vector3.forward);
        parentScale = new Vector3(singularX, singularY, 1f);
        childRotation = Quaternion.AngleAxis(-vAngle * Mathf.Rad2Deg, Vector3.forward);

        return true;
    }

    /// <summary>
    /// The FULL 3D version of the same trick: factors the parallelepiped spanned by three edge vectors
    /// into parent rotation U + parent scale Sigma and child rotation V^T, via a real 3x3 SVD
    /// (A = [u|v|w] = U * Sigma * V^T). Where the 2D rig shears within one plane, this rig skews all
    /// three axes at once — a cube becomes an arbitrarily slanted parallelepiped, still only 2 toys.
    /// <paramref name="center"/> is the offset from the origin corner to the solid's center — place
    /// the PARENT at origin + center. Returns false for degenerate (coplanar/zero-volume) edge sets.
    /// </summary>
    public static bool TryGetShearRig3D(
        Vector3 edgeX,
        Vector3 edgeY,
        Vector3 edgeZ,
        out Vector3 center,
        out Quaternion parentRotation,
        out Vector3 parentScale,
        out Quaternion childRotation)
    {
        center = (edgeX + edgeY + edgeZ) * 0.5f;
        parentRotation = Quaternion.identity;
        parentScale = Vector3.one;
        childRotation = Quaternion.identity;

        // Column matrix A = [u|v|w]; a near-zero determinant means coplanar edges = zero-volume solid.
        double[,] a =
        {
            { edgeX.x, edgeY.x, edgeZ.x },
            { edgeX.y, edgeY.y, edgeZ.y },
            { edgeX.z, edgeY.z, edgeZ.z },
        };

        double det =
            (a[0, 0] * ((a[1, 1] * a[2, 2]) - (a[1, 2] * a[2, 1])))
            - (a[0, 1] * ((a[1, 0] * a[2, 2]) - (a[1, 2] * a[2, 0])))
            + (a[0, 2] * ((a[1, 0] * a[2, 1]) - (a[1, 1] * a[2, 0])));
        if (Math.Abs(det) < 0.000001)
        {
            return false;
        }

        // A cube child mirrors symmetrically, so a negative-determinant basis can be fixed by swapping
        // two columns (v<->w): same solid, right-handed frame, and both U and V stay pure rotations.
        if (det < 0)
        {
            for (int row = 0; row < 3; row++)
            {
                (a[row, 1], a[row, 2]) = (a[row, 2], a[row, 1]);
            }
        }

        if (!GetSvd3x3(a, out Quaternion uRotation, out Vector3 sigma, out Quaternion vRotation))
        {
            return false;
        }

        if (sigma.x < 0.000001f || sigma.y < 0.000001f || sigma.z < 0.000001f)
        {
            return false;
        }

        parentRotation = uRotation;
        parentScale = sigma;
        childRotation = Quaternion.Inverse(vRotation); // V^T

        return true;
    }

    /// <summary>
    /// SVD of a real 3x3 matrix via a cyclic Jacobi eigen-decomposition of A^T*A (doubles for the
    /// iteration; toy-scale inputs converge in a handful of sweeps). Outputs U and V as quaternions
    /// with det = +1 (proper rotations) — the caller guarantees det(A) > 0.
    /// </summary>
    private static bool GetSvd3x3(double[,] a, out Quaternion uRotation, out Vector3 sigma, out Quaternion vRotation)
    {
        uRotation = Quaternion.identity;
        sigma = Vector3.one;
        vRotation = Quaternion.identity;

        // S = A^T * A (symmetric positive semi-definite).
        double[,] s = new double[3, 3];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                s[i, j] = (a[0, i] * a[0, j]) + (a[1, i] * a[1, j]) + (a[2, i] * a[2, j]);
            }
        }

        // Cyclic Jacobi: V accumulates the plane rotations that diagonalize S.
        double[,] v = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 32; sweep++)
        {
            double off = (s[0, 1] * s[0, 1]) + (s[0, 2] * s[0, 2]) + (s[1, 2] * s[1, 2]);
            if (off < 1e-18)
            {
                break;
            }

            for (int p = 0; p < 2; p++)
            {
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(s[p, q]) < 1e-15)
                    {
                        continue;
                    }

                    double theta = (s[q, q] - s[p, p]) / (2.0 * s[p, q]);
                    double t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1.0));
                    double c = 1.0 / Math.Sqrt((t * t) + 1.0);
                    double sn = t * c;

                    for (int k = 0; k < 3; k++)
                    {
                        double skp = s[k, p];
                        double skq = s[k, q];
                        s[k, p] = (c * skp) - (sn * skq);
                        s[k, q] = (sn * skp) + (c * skq);
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        double spk = s[p, k];
                        double sqk = s[q, k];
                        s[p, k] = (c * spk) - (sn * sqk);
                        s[q, k] = (sn * spk) + (c * sqk);

                        double vkp = v[k, p];
                        double vkq = v[k, q];
                        v[k, p] = (c * vkp) - (sn * vkq);
                        v[k, q] = (sn * vkp) + (c * vkq);
                    }
                }
            }
        }

        // Sort eigenvalues descending, carrying V's columns along.
        double[] lambda = { Math.Max(s[0, 0], 0), Math.Max(s[1, 1], 0), Math.Max(s[2, 2], 0) };
        for (int i = 0; i < 2; i++)
        {
            for (int j = i + 1; j < 3; j++)
            {
                if (lambda[j] > lambda[i])
                {
                    (lambda[i], lambda[j]) = (lambda[j], lambda[i]);
                    for (int k = 0; k < 3; k++)
                    {
                        (v[k, i], v[k, j]) = (v[k, j], v[k, i]);
                    }
                }
            }
        }

        double s0 = Math.Sqrt(lambda[0]);
        double s1 = Math.Sqrt(lambda[1]);
        double s2 = Math.Sqrt(lambda[2]);
        if (s2 < 0.000001)
        {
            return false;
        }

        // det(V) = +1: V came from rotations, but the sort can swap parity — flip one column if so.
        FixHandedness(v);

        // U = A * V * Sigma^-1, column by column.
        double[] singular = { s0, s1, s2 };
        double[,] u = new double[3, 3];
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                double sum = 0;
                for (int k = 0; k < 3; k++)
                {
                    sum += a[row, k] * v[k, col];
                }

                u[row, col] = sum / singular[col];
            }
        }

        // det(A) > 0 and det(V) = +1 make U a proper rotation up to round-off; re-orthonormalize anyway.
        Orthonormalize(u);

        uRotation = ToQuaternion(u);
        vRotation = ToQuaternion(v);
        sigma = new Vector3((float)s0, (float)s1, (float)s2);
        return true;
    }

    private static void FixHandedness(double[,] m)
    {
        double det =
            (m[0, 0] * ((m[1, 1] * m[2, 2]) - (m[1, 2] * m[2, 1])))
            - (m[0, 1] * ((m[1, 0] * m[2, 2]) - (m[1, 2] * m[2, 0])))
            + (m[0, 2] * ((m[1, 0] * m[2, 1]) - (m[1, 1] * m[2, 0])));
        if (det < 0)
        {
            for (int row = 0; row < 3; row++)
            {
                m[row, 2] = -m[row, 2];
            }
        }
    }

    /// <summary>Gram-Schmidt on the columns (cleans up the round-off from the Sigma^-1 division).</summary>
    private static void Orthonormalize(double[,] m)
    {
        NormalizeColumn(m, 0);

        double dot01 = ColumnDot(m, 0, 1);
        for (int row = 0; row < 3; row++)
        {
            m[row, 1] -= dot01 * m[row, 0];
        }

        NormalizeColumn(m, 1);

        // Column 2 = col0 x col1 guarantees a right-handed orthonormal frame.
        m[0, 2] = (m[1, 0] * m[2, 1]) - (m[2, 0] * m[1, 1]);
        m[1, 2] = (m[2, 0] * m[0, 1]) - (m[0, 0] * m[2, 1]);
        m[2, 2] = (m[0, 0] * m[1, 1]) - (m[1, 0] * m[0, 1]);
    }

    private static void NormalizeColumn(double[,] m, int col)
    {
        double length = Math.Sqrt((m[0, col] * m[0, col]) + (m[1, col] * m[1, col]) + (m[2, col] * m[2, col]));
        if (length > 1e-12)
        {
            m[0, col] /= length;
            m[1, col] /= length;
            m[2, col] /= length;
        }
    }

    private static double ColumnDot(double[,] m, int colA, int colB)
        => (m[0, colA] * m[0, colB]) + (m[1, colA] * m[1, colB]) + (m[2, colA] * m[2, colB]);

    /// <summary>Rotation-matrix (columns = basis vectors) to quaternion, Unity component order.</summary>
    private static Quaternion ToQuaternion(double[,] m)
    {
        // Shepperd's method: pick the largest diagonal combination for numerical safety.
        double trace = m[0, 0] + m[1, 1] + m[2, 2];
        double qw, qx, qy, qz;

        if (trace > 0)
        {
            double r = Math.Sqrt(1.0 + trace);
            double inv = 0.5 / r;
            qw = 0.5 * r;
            qx = (m[2, 1] - m[1, 2]) * inv;
            qy = (m[0, 2] - m[2, 0]) * inv;
            qz = (m[1, 0] - m[0, 1]) * inv;
        }
        else if (m[0, 0] >= m[1, 1] && m[0, 0] >= m[2, 2])
        {
            double r = Math.Sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]);
            double inv = 0.5 / r;
            qx = 0.5 * r;
            qw = (m[2, 1] - m[1, 2]) * inv;
            qy = (m[0, 1] + m[1, 0]) * inv;
            qz = (m[0, 2] + m[2, 0]) * inv;
        }
        else if (m[1, 1] >= m[2, 2])
        {
            double r = Math.Sqrt(1.0 - m[0, 0] + m[1, 1] - m[2, 2]);
            double inv = 0.5 / r;
            qy = 0.5 * r;
            qw = (m[0, 2] - m[2, 0]) * inv;
            qx = (m[0, 1] + m[1, 0]) * inv;
            qz = (m[1, 2] + m[2, 1]) * inv;
        }
        else
        {
            double r = Math.Sqrt(1.0 - m[0, 0] - m[1, 1] + m[2, 2]);
            double inv = 0.5 / r;
            qz = 0.5 * r;
            qw = (m[1, 0] - m[0, 1]) * inv;
            qx = (m[0, 2] + m[2, 0]) * inv;
            qy = (m[1, 2] + m[2, 1]) * inv;
        }

        return new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
    }

    /// <summary>Closed-form SVD of the 2x2 matrix [[a, b], [c, d]] via the eigen-decomposition of A^T*A.</summary>
    private static void GetSvd2x2(float a, float b, float c, float d, out float uAngle, out float singularX, out float singularY, out float vAngle)
    {
        float b00 = (a * a) + (c * c);
        float b01 = (a * b) + (c * d);
        float b11 = (b * b) + (d * d);

        float trace = b00 + b11;
        float diff = b00 - b11;
        float root = Mathf.Sqrt((diff * diff) + (4f * b01 * b01));
        float lambdaX = Mathf.Max((trace + root) * 0.5f, 0f);
        float lambdaY = Mathf.Max((trace - root) * 0.5f, 0f);

        singularX = Mathf.Sqrt(lambdaX);
        singularY = Mathf.Sqrt(lambdaY);

        Vector2 v1;
        if (Mathf.Abs(b01) > 0.000001f)
        {
            v1 = new Vector2(lambdaX - b11, b01).normalized;
        }
        else
        {
            v1 = b00 >= b11 ? Vector2.right : Vector2.up;
        }

        Vector2 av1 = new((a * v1.x) + (b * v1.y), (c * v1.x) + (d * v1.y));
        Vector2 u1 = av1 / singularX;

        vAngle = Mathf.Atan2(v1.y, v1.x);
        uAngle = Mathf.Atan2(u1.y, u1.x);
    }
}
