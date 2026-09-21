using UnityEngine;

namespace ByAWhisker.Visibility
{
    /// <summary>
    /// 부채꼴 광선으로 보이는 영역을 계산한다. 순수 계산과 메시 생성만 한다.
    /// 상태를 안 가져야 적과 플레이어가 같은 판정을 쓸 수 있어서 static으로 뒀다.
    /// </summary>
    public static class VisionPolygon
    {
        /// <summary>
        /// 몸 주변 원을 도는 광선 수. 원뿔처럼 촘촘할 필요가 없어서 성기게 고정했다.
        /// 버퍼 크기를 잡을 때 필요하니 공개해 둔다.
        /// </summary>
        public const int NearRayCount = 24;

        /// <summary>막힌 지점보다 이만큼 더 바깥을 정점으로 쓴다. 벽면이 마스크에 덮이게 하려고.</summary>
        private const float WallBias = 0.05f;

        // 메시 채우기용 재사용 배열. 정점 수가 늘 때만 새로 잡아서 매 프레임 할당이 없다.
        private static Vector3[] _vertices;
        private static int[] _triangles;

        /// <summary>
        /// 눈 위치에서 시작해 정면 원뿔과 몸 주변 원을 합친 폴리곤 정점을 구한다.
        /// 결과는 월드 좌표이고, 첫 정점은 중심(눈 위치)이다. 반환값은 채운 정점 수.
        /// 정점은 각도 순으로 정렬되어 있어서 그대로 삼각 팬이 된다.
        /// </summary>
        public static int Build(
            Vector3 origin, float yawDegrees, float coneDegrees, float radius,
            float nearRadius, int rayCount, LayerMask blockers,
            Vector3[] buffer)
        {
            if (buffer == null || buffer.Length < 3) return 0;

            rayCount = Mathf.Max(2, rayCount);
            coneDegrees = Mathf.Clamp(coneDegrees, 1f, 360f);
            radius = Mathf.Max(0.01f, radius);
            nearRadius = Mathf.Clamp(nearRadius, 0f, radius);

            int count = 0;
            buffer[count++] = origin; // 삼각 팬의 꼭지점

            float half = coneDegrees * 0.5f;
            float coneStep = coneDegrees / (rayCount - 1); // 양 끝 각도를 정확히 포함시키려고 -1로 나눈다
            float nearStep = 360f / NearRayCount;

            // 두 목록 모두 yaw 기준 상대각이 오름차순이라, 병합만 하면 정렬이 끝난다.
            // 나중에 따로 Sort를 돌리면 비교자 할당이 생길 수 있어서 이 방식을 골랐다.
            int cone = 0;
            int near = 0;

            while (count < buffer.Length)
            {
                // 원뿔이 덮는 각도의 원 광선은 버린다. 같은 각을 더 멀리 보는 원뿔 광선이 이미 있어서
                // 그냥 섞으면 폴리곤이 톱니처럼 파인다.
                while (near < NearRayCount && Mathf.Abs(-180f + nearStep * near) <= half) near++;

                bool hasCone = cone < rayCount;
                bool hasNear = near < NearRayCount;
                if (!hasCone && !hasNear) break;

                float coneAngle = hasCone ? -half + coneStep * cone : float.MaxValue;
                float nearAngle = hasNear ? -180f + nearStep * near : float.MaxValue;

                float angle;
                float reach;
                if (coneAngle <= nearAngle)
                {
                    angle = coneAngle;
                    reach = radius;
                    cone++;
                }
                else
                {
                    angle = nearAngle;
                    reach = nearRadius;
                    near++;
                }

                buffer[count++] = CastVertex(origin, yawDegrees + angle, reach, blockers);
            }

            return count;
        }

        /// <summary>buffer의 정점으로 삼각 팬 메시를 채운다. mesh는 재사용한다.</summary>
        public static void FillMesh(Mesh mesh, Vector3[] buffer, int count, Vector3 origin)
        {
            if (mesh == null || buffer == null) return;

            mesh.Clear();
            if (count < 3 || count > buffer.Length) return;

            EnsureCapacity(count);

            float extent = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 local = buffer[i] - origin;
                local.y = 0f; // 위에서 내려다보며 그릴 거라 평면에 눕힌다
                _vertices[i] = local;

                float reach = Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.z));
                if (reach > extent) extent = reach;
            }

            int perimeter = count - 1;
            int index = 0;
            for (int i = 1; i <= perimeter; i++)
            {
                // 마지막 정점은 처음으로 돌아가 고리를 닫는다. 몸 주변 원이 360도를 다 덮으니까.
                int next = i < perimeter ? i + 1 : 1;
                _triangles[index++] = 0;
                _triangles[index++] = i;
                _triangles[index++] = next;
            }

            if (mesh.subMeshCount != 1) mesh.subMeshCount = 1;
            mesh.SetVertices(_vertices, 0, count);
            mesh.SetTriangles(_triangles, 0, index, 0, false);

            // RecalculateBounds는 정점을 한 번 더 훑는다. 위에서 이미 잰 크기를 그대로 넣는다.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(extent * 2f, 1f, extent * 2f));
        }

        /// <summary>한 방향으로 쏴서 정점 하나를 얻는다. 막히면 조금 더 바깥을 쓴다.</summary>
        private static Vector3 CastVertex(Vector3 origin, float angleDegrees, float reach, LayerMask blockers)
        {
            if (reach <= 0.001f) return origin;

            float radians = angleDegrees * Mathf.Deg2Rad;
            // Unity의 yaw 0도는 +Z다. 그래서 x가 sin, z가 cos이다.
            Vector3 direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));

            RaycastHit hit;
            // 트리거는 무시한다. 출구나 체크포인트 볼륨이 시야를 막으면 곤란하다. Sight와 같은 규칙이다.
            if (Physics.Raycast(origin, direction, out hit, reach, blockers.value, QueryTriggerInteraction.Ignore))
            {
                return hit.point + direction * WallBias;
            }

            return origin + direction * reach;
        }

        /// <summary>정점 수가 늘 때만 배열을 새로 잡는다. rayCount가 고정이면 첫 프레임 뒤로 할당이 없다.</summary>
        private static void EnsureCapacity(int count)
        {
            if (_vertices == null || _vertices.Length < count)
            {
                _vertices = new Vector3[count];
            }

            int needed = count * 3; // 둘레 정점 수 = count - 1, 삼각형도 같은 수라 3배면 넉넉하다
            if (_triangles == null || _triangles.Length < needed)
            {
                _triangles = new int[needed];
            }
        }
    }
}
