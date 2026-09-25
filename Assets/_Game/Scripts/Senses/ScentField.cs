using System.Collections.Generic;
using UnityEngine;

namespace ByAWhisker.Senses
{
    /// <summary>
    /// 한 지점에서 읽은 냄새. 값만 담는 구조체라 질의할 때 할당이 생기지 않는다.
    /// </summary>
    public struct ScentReading
    {
        /// <summary>0..1</summary>
        public float strength;

        /// <summary>짙어지는 쪽 수평 방향. 냄새가 없거나 평평하면 zero다.</summary>
        public Vector3 gradient;

        /// <summary>가장 짙은 냄새의 주인. ScentSource.OwnerId와 같은 값이다.</summary>
        public int ownerId;

        /// <summary>이 자리에 남은 지 몇 초 됐는지. 자취를 쫓을 때 어느 쪽이 최근인지 가른다.</summary>
        public float age;
    }

    /// <summary>
    /// 격자 냄새 확산. 칸마다 세기와 주인과 남은 시간을 들고 있다.
    /// 세 가지를 한다. 원천이 자기 칸에 세기를 더하고, 바람 방향으로 흘리고, 시간에 따라 옅어진다.
    ///
    /// 벽은 막지 않는다. 일부러 그렇게 둔다. 냄새가 벽을 돌아 흘러오는 것이 이 게임에서 후각의 쓸모다.
    /// 시각은 벽에서 끊기고 청각은 거리로 끊기는데 후각만 벽을 넘으니 세 감각이 서로 다른 정보를 준다.
    ///
    /// 갱신 비용은 격자 크기가 아니라 "냄새가 실제로 있는 칸 수"에 비례한다.
    /// 큰 레벨에서 빈 칸까지 매번 훑으면 128x128만 해도 초당 8만 칸이 되는데, 그중 냄새가 있는 곳은
    /// 원천 주변 수십 칸뿐이다. 그래서 활성 칸 목록만 들고 돌린다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScentField : MonoBehaviour
    {
        // 한 번 갱신할 때 냄새가 옮겨 갈 수 있는 최대 칸 수.
        // 이보다 멀리 뛰면 갱신과 갱신 사이가 비어서 자취가 점선으로 끊긴다.
        private const float MaxCellsPerStep = 1.5f;

        // Configure 전에 활성 칸을 물어보는 쪽에 돌려줄 빈 목록. 매번 새로 만들면 그것부터 할당이다.
        private static readonly int[] EmptyCells = new int[0];

        [Header("갱신")]
        [Tooltip("갱신 간격. 초. 매 프레임 돌릴 이유가 없다. 냄새는 천천히 움직인다.")]
        [SerializeField] private float updateInterval = 0.2f;

        [Tooltip("바람 에셋. 비워 두면 바람 없이 제자리에서 번지기만 한다.")]
        [SerializeField] private WindSettings wind;

        [Header("확산")]
        [Tooltip("원천이 1초 동안 자기 칸에 더하는 양. 원천의 Strength가 곱해진다.")]
        [SerializeField] private float depositPerSecond = 2f;

        [Tooltip("초당 옅어지는 비율. 클수록 자취가 짧다. 0.35면 한 번 남은 냄새가 대략 8초쯤 간다.")]
        [SerializeField] private float decayPerSecond = 0.35f;

        [Tooltip("한 번 갱신할 때 옆 칸으로 새어 나가는 비율. 바람이 없어도 이만큼은 번진다.")]
        [Range(0f, 0.5f)] [SerializeField] private float spreadPerStep = 0.18f;

        [Tooltip("이보다 옅으면 없는 것으로 친다. 이 값이 활성 칸 수의 상한을 실질적으로 정한다.")]
        [SerializeField] private float minStrength = 0.004f;

        [Header("한계")]
        [Tooltip("동시에 냄새를 들고 있을 수 있는 최대 칸 수. 최악의 경우에도 갱신 비용이 여기서 멈춘다.")]
        [SerializeField] private int maxActiveCells = 4096;

        [Tooltip("격자 칸 수 상한. 넘으면 Configure가 칸 크기를 키워서 맞춘다.")]
        [SerializeField] private int maxGridCells = 262144;

        [Header("기본 범위")]
        [Tooltip("통합 담당이 Configure를 부르지 않을 때 이 값으로 시작한다.")]
        [SerializeField] private bool autoConfigureOnStart = true;
        [SerializeField] private Vector3 defaultWorldMin = new Vector3(-64f, 0f, -64f);
        [SerializeField] private Vector3 defaultWorldSize = new Vector3(128f, 8f, 128f);
        [SerializeField] private float defaultCellSize = 1f;

        // 읽는 쪽과 쓰는 쪽을 나눈 뒤 갱신이 끝나면 맞바꾼다.
        // 한 배열에서 읽으면서 쓰면 이미 흘려보낸 냄새를 같은 갱신에서 또 흘리게 된다.
        private float[] _readStrength;
        private float[] _writeStrength;
        private int[] _readOwner;
        private int[] _writeOwner;
        private float[] _readAge;
        private float[] _writeAge;

        // 이번 갱신에 그 칸에 들어온 기여 중 가장 큰 값. 주인과 나이를 누가 가져갈지 고르는 데 쓴다.
        private float[] _bestAmount;

        // 마지막으로 쓰인 갱신 번호. 이걸로 "이번에 처음 쓰는 칸"을 가려내면
        // 갱신마다 배열 전체를 0으로 지우지 않아도 된다. 큰 격자에서 이 비용이 제일 컸다.
        private int[] _stamp;
        private int _tick;

        private List<int> _readActive;
        private List<int> _writeActive;

        private int _cols;
        private int _rows;
        private float _cellSize = 1f;
        private float _minX;
        private float _minZ;
        private int _activeLimit;
        private float _timer;
        private bool _configured;

        // 인스펙터에서 minStrength를 0으로 두면 0짜리 기여가 칸을 열어 활성 목록이 상한까지 차 버린다.
        // 갱신마다 한 번만 바닥을 씌워 두고 안에서는 이 값만 본다.
        private float _minStrength = 0.004f;

        private Vector4 _worldBounds;

        /// <summary>Configure가 끝나 격자가 준비되었는지.</summary>
        public bool IsConfigured { get { return _configured; } }

        /// <summary>(minX, minZ, sizeX, sizeZ)</summary>
        public Vector4 WorldBounds { get { return _worldBounds; } }

        /// <summary>격자 열 수. 칸을 그대로 픽셀로 옮기는 쪽이 텍스처 폭으로 쓴다.</summary>
        public int GridCols { get { return _cols; } }

        /// <summary>격자 행 수.</summary>
        public int GridRows { get { return _rows; } }

        /// <summary>칸 한 변의 길이. Configure가 상한 때문에 키웠을 수 있어 넘긴 값과 다를 수 있다.</summary>
        public float CellSize { get { return _cellSize; } }

        /// <summary>
        /// 지금 냄새를 들고 있는 칸의 번호들. 번호는 row * GridCols + col이다.
        /// 이 클래스가 격자 크기와 무관한 비용으로 도는 이유가 이 목록이니, 받아 가는 쪽도 같은 약속을 지켜야 한다.
        /// 여기 없는 칸을 확인하겠다고 격자 전체를 훑으면 128x128에서 초당 8만 칸이 되어 이득이 통째로 사라진다.
        /// 목록은 갱신마다 통째로 바뀐다. 들고 있지 말고 쓸 때마다 다시 물어라.
        /// </summary>
        public IReadOnlyList<int> ActiveCells
        {
            get { return _readActive != null ? (IReadOnlyList<int>)_readActive : EmptyCells; }
        }

        /// <summary>
        /// 칸 번호로 그 칸의 값을 읽는다. ActiveCells와 짝으로 쓴다. 읽을 만한 냄새가 없으면 false다.
        /// ScentReading을 돌려주지 않는 이유는 기울기다. 기울기 한 번에 표집이 네 번 더 드는데,
        /// 칸을 통째로 훑는 쪽은 방향이 필요 없다. 방향까지 필요한 곳은 지금처럼 TrySample을 쓴다.
        /// </summary>
        public bool TryReadCell(int cellIndex, out float strength, out int ownerId, out float age)
        {
            strength = 0f;
            ownerId = 0;
            age = 0f;

            if (!_configured) return false;
            if (cellIndex < 0 || cellIndex >= _readStrength.Length) return false;

            strength = _readStrength[cellIndex];
            if (strength < _minStrength)
            {
                strength = 0f;
                return false;
            }

            ownerId = _readOwner[cellIndex];
            age = _readAge[cellIndex];
            return true;
        }

        private void Start()
        {
            if (!_configured && autoConfigureOnStart)
            {
                Configure(defaultWorldMin, defaultWorldSize, defaultCellSize);
            }
        }

        /// <summary>
        /// 레벨 크기에 맞춰 격자를 잡는다. 통합 담당이 시작할 때 부른다.
        /// 격자와 버퍼는 여기서 한 번만 잡고 그 뒤로는 계속 재사용한다.
        /// </summary>
        public void Configure(Vector3 worldMin, Vector3 worldSize, float cellSize)
        {
            float sizeX = Mathf.Max(0.01f, worldSize.x);
            float sizeZ = Mathf.Max(0.01f, worldSize.z);
            float cell = cellSize > 0.01f ? cellSize : 1f;

            int cols = Mathf.Max(1, Mathf.CeilToInt(sizeX / cell));
            int rows = Mathf.Max(1, Mathf.CeilToInt(sizeZ / cell));

            // 큰 레벨에 작은 칸을 주면 메모리가 먼저 터진다. 조용히 죽는 대신 칸을 키우고 알려 준다.
            int limit = Mathf.Max(64, maxGridCells);
            if (cols * rows > limit)
            {
                float scale = Mathf.Sqrt((float)(cols * rows) / limit);
                cell *= scale;
                cols = Mathf.Max(1, Mathf.CeilToInt(sizeX / cell));
                rows = Mathf.Max(1, Mathf.CeilToInt(sizeZ / cell));
                Debug.LogWarning($"[ScentField] 칸이 너무 많아 칸 크기를 {cell:0.00}m로 키웠다. " +
                                 $"격자 {cols}x{rows}. Configure에 더 큰 cellSize를 주는 편이 낫다.", this);
            }

            _cols = cols;
            _rows = rows;
            _cellSize = cell;
            _minX = worldMin.x;
            _minZ = worldMin.z;

            // 격자 밖으로 삐져나간 만큼은 경계 칸이 먹는다. 보고하는 범위는 실제 칸이 덮는 넓이다.
            _worldBounds = new Vector4(_minX, _minZ, _cols * _cellSize, _rows * _cellSize);

            int count = _cols * _rows;
            _activeLimit = Mathf.Clamp(maxActiveCells, 16, count);

            AllocateGrid(count);

            _tick = 0;
            _timer = 0f;
            _minStrength = Mathf.Max(minStrength, 1e-5f);
            _configured = true;

            Clear();
        }

        /// <summary>재시작할 때 부른다. 남아 있던 냄새를 모두 지운다.</summary>
        public void Clear()
        {
            if (!_configured) return;

            System.Array.Clear(_readStrength, 0, _readStrength.Length);
            System.Array.Clear(_writeStrength, 0, _writeStrength.Length);
            System.Array.Clear(_readOwner, 0, _readOwner.Length);
            System.Array.Clear(_writeOwner, 0, _writeOwner.Length);
            System.Array.Clear(_readAge, 0, _readAge.Length);
            System.Array.Clear(_writeAge, 0, _writeAge.Length);
            System.Array.Clear(_bestAmount, 0, _bestAmount.Length);
            System.Array.Clear(_stamp, 0, _stamp.Length);

            _readActive.Clear();
            _writeActive.Clear();

            // 도장을 0으로 되돌렸으니 번호도 같이 되돌린다. 안 그러면 첫 갱신이 옛 도장을 새것으로 본다.
            _tick = 0;
            _timer = 0f;
        }

        /// <summary>이 자리의 냄새 세기만 빠르게 본다. 0..1.</summary>
        public float Sample(Vector3 worldPoint)
        {
            if (!_configured) return 0f;

            float cx = (worldPoint.x - _minX) / _cellSize - 0.5f;
            float cz = (worldPoint.z - _minZ) / _cellSize - 0.5f;
            return SampleBilinear(cx, cz);
        }

        /// <summary>
        /// 세기와 방향과 주인까지 읽는다.
        /// 격자 밖이거나 읽을 만한 냄새가 없으면 false를 준다. 부르는 쪽이 세기를 또 검사하지 않아도 되게 한다.
        /// </summary>
        public bool TrySample(Vector3 worldPoint, out ScentReading reading)
        {
            reading = default(ScentReading);
            if (!_configured) return false;

            int col = Mathf.FloorToInt((worldPoint.x - _minX) / _cellSize);
            int row = Mathf.FloorToInt((worldPoint.z - _minZ) / _cellSize);
            if (col < 0 || row < 0 || col >= _cols || row >= _rows) return false;

            float cx = (worldPoint.x - _minX) / _cellSize - 0.5f;
            float cz = (worldPoint.z - _minZ) / _cellSize - 0.5f;

            float strength = SampleBilinear(cx, cz);
            if (strength < _minStrength) return false;

            // 기울기는 한 칸 떨어진 양쪽 값의 차로 구한다. 한 칸보다 촘촘히 보면 격자 무늬가 방향에 그대로 드러난다.
            float dx = SampleBilinear(cx + 1f, cz) - SampleBilinear(cx - 1f, cz);
            float dz = SampleBilinear(cx, cz + 1f) - SampleBilinear(cx, cz - 1f);

            Vector3 gradient = new Vector3(dx, 0f, dz);
            float gradientSqr = gradient.sqrMagnitude;
            gradient = gradientSqr > 1e-10f ? gradient / Mathf.Sqrt(gradientSqr) : Vector3.zero;

            int index = row * _cols + col;

            reading.strength = strength;
            reading.gradient = gradient;
            reading.ownerId = _readOwner[index];
            reading.age = _readAge[index];
            return true;
        }

        private void Update()
        {
            if (!_configured) return;

            _timer += Time.deltaTime;
            if (_timer < updateInterval) return;

            float dt = _timer;
            _timer = 0f;

            // 로딩이나 브레이크포인트로 프레임이 길게 멈췄을 때 몇 초치를 한 번에 흘리면 냄새가 순간이동한다.
            // 못 돌린 시간은 그냥 버린다. 따라잡겠다고 여러 번 돌리면 그 프레임이 더 길어진다.
            float maxStep = updateInterval * 4f;
            if (dt > maxStep) dt = maxStep;

            Step(dt);
        }

        private void Step(float dt)
        {
            _tick++;
            _writeActive.Clear();
            _minStrength = Mathf.Max(minStrength, 1e-5f);

            Vector3 windVelocity = wind != null ? wind.SampleAt(Time.time) : Vector3.zero;

            float offsetCol = 0f;
            float offsetRow = 0f;
            if (windVelocity.sqrMagnitude > 1e-8f)
            {
                offsetCol = windVelocity.x * dt / _cellSize;
                offsetRow = windVelocity.z * dt / _cellSize;

                float travel = Mathf.Sqrt(offsetCol * offsetCol + offsetRow * offsetRow);
                if (travel > MaxCellsPerStep)
                {
                    float scale = MaxCellsPerStep / travel;
                    offsetCol *= scale;
                    offsetRow *= scale;
                }
            }

            // 프레임 간격이 흔들려도 같은 속도로 옅어지도록 지수 감쇠를 쓴다.
            float decay = Mathf.Exp(-decayPerSecond * dt);
            float spread = Mathf.Clamp(spreadPerStep, 0f, 0.5f);

            for (int i = 0; i < _readActive.Count; i++)
            {
                int index = _readActive[i];

                float carried = _readStrength[index] * decay;
                if (carried < _minStrength) continue;   // 여기서 걸러진 칸은 다음 목록에 실리지 않는다

                int col = index % _cols;
                int row = index / _cols;
                int owner = _readOwner[index];
                float age = _readAge[index] + dt;

                float leak = carried * spread;
                float core = carried - leak;

                // 바람을 탄 지점은 칸 경계에 딱 맞지 않는다. 네 칸에 나눠 담아야 자취가 계단처럼 꺾이지 않는다.
                ScatterBilinear(col + offsetCol, row + offsetRow, core, owner, age);

                if (leak > 0f)
                {
                    float quarter = leak * 0.25f;
                    Deposit(col + 1, row, quarter, owner, age, false);
                    Deposit(col - 1, row, quarter, owner, age, false);
                    Deposit(col, row + 1, quarter, owner, age, false);
                    Deposit(col, row - 1, quarter, owner, age, false);
                }
            }

            // 원천이 자기 칸에 새 냄새를 더한다. 흘린 뒤에 더해야 갓 남긴 냄새가 원천 자리에 제대로 남는다.
            IReadOnlyList<ScentSource> sources = ScentSource.All;
            if (sources != null)
            {
                // foreach는 인터페이스 열거자를 박싱한다. 인덱스로 돈다.
                for (int i = 0; i < sources.Count; i++)
                {
                    ScentSource source = sources[i];
                    if (source == null) continue;

                    float amount = source.Strength * depositPerSecond * dt;
                    if (amount < _minStrength) continue;

                    Vector3 position = source.transform.position;
                    int col = Mathf.FloorToInt((position.x - _minX) / _cellSize);
                    int row = Mathf.FloorToInt((position.z - _minZ) / _cellSize);

                    Deposit(col, row, amount, source.OwnerId, 0f, true);
                }
            }

            SwapBuffers();
        }

        /// <summary>실수 칸 좌표에 이중선형으로 나눠 담는다.</summary>
        private void ScatterBilinear(float col, float row, float amount, int owner, float age)
        {
            if (amount < _minStrength) return;

            int c0 = Mathf.FloorToInt(col);
            int r0 = Mathf.FloorToInt(row);
            float fx = col - c0;
            float fz = row - r0;
            float ix = 1f - fx;
            float iz = 1f - fz;

            Deposit(c0, r0, amount * ix * iz, owner, age, false);
            if (fx > 0f) Deposit(c0 + 1, r0, amount * fx * iz, owner, age, false);
            if (fz > 0f) Deposit(c0, r0 + 1, amount * ix * fz, owner, age, false);
            if (fx > 0f && fz > 0f) Deposit(c0 + 1, r0 + 1, amount * fx * fz, owner, age, false);
        }

        /// <summary>
        /// 한 칸에 기여를 더한다. 벽을 보지 않는다. 냄새는 벽을 돈다.
        /// claim이 참이면 기여가 작아도 주인과 나이를 가져간다. 원천이 직접 남기는 냄새에만 쓴다.
        /// </summary>
        private void Deposit(int col, int row, float amount, int owner, float age, bool claim)
        {
            // 티끌은 버린다. 안 버리면 활성 목록만 계속 늘어나고 화면에는 아무 차이도 안 난다.
            if (amount < _minStrength) return;
            if (col < 0 || row < 0 || col >= _cols || row >= _rows) return;

            int index = row * _cols + col;

            if (_stamp[index] != _tick)
            {
                // 상한을 넘으면 새 칸은 열지 않는다. 이미 열린 칸은 계속 받는다.
                // 냄새가 사라지는 대신 번지는 범위만 멈추므로, 터지는 것보다 티가 덜 난다.
                if (_writeActive.Count >= _activeLimit) return;

                _stamp[index] = _tick;
                _writeStrength[index] = 0f;
                _writeOwner[index] = 0;
                _writeAge[index] = 0f;
                _bestAmount[index] = 0f;
                _writeActive.Add(index);
            }

            float sum = _writeStrength[index] + amount;
            _writeStrength[index] = sum > 1f ? 1f : sum;

            // 주인과 나이는 가장 많이 기여한 쪽을 따른다. 섞어서 평균을 내면 아무의 것도 아닌 값이 되어
            // 추적하는 쪽이 "누구 냄새인가"에 답할 수 없다.
            //
            // 원천이 직접 남기는 냄새(claim)는 양과 상관없이 이긴다. 한자리에 오래 서 있으면
            // 그 칸에는 자기가 전에 남긴 짙고 오래된 냄새가 쌓여 있어서, 양으로만 겨루면
            // "지금 여기서 냄새가 나는 중"인 칸이 가장 오래된 칸으로 읽힌다. 그러면 자취 추적이 거꾸로 간다.
            if (claim || amount > _bestAmount[index])
            {
                if (amount > _bestAmount[index]) _bestAmount[index] = amount;
                _writeOwner[index] = owner;
                _writeAge[index] = age;
            }
        }

        private void SwapBuffers()
        {
            float[] strength = _readStrength;
            _readStrength = _writeStrength;
            _writeStrength = strength;

            int[] owner = _readOwner;
            _readOwner = _writeOwner;
            _writeOwner = owner;

            float[] age = _readAge;
            _readAge = _writeAge;
            _writeAge = age;

            List<int> active = _readActive;
            _readActive = _writeActive;
            _writeActive = active;
        }

        private float SampleBilinear(float col, float row)
        {
            int c0 = Mathf.FloorToInt(col);
            int r0 = Mathf.FloorToInt(row);
            float fx = col - c0;
            float fz = row - r0;

            float s00 = StrengthAt(c0, r0);
            float s10 = StrengthAt(c0 + 1, r0);
            float s01 = StrengthAt(c0, r0 + 1);
            float s11 = StrengthAt(c0 + 1, r0 + 1);

            float bottom = s00 + (s10 - s00) * fx;
            float top = s01 + (s11 - s01) * fx;
            return bottom + (top - bottom) * fz;
        }

        /// <summary>격자 밖은 냄새가 없는 것으로 본다.</summary>
        private float StrengthAt(int col, int row)
        {
            if (col < 0 || row < 0 || col >= _cols || row >= _rows) return 0f;
            return _readStrength[row * _cols + col];
        }

        private void AllocateGrid(int count)
        {
            // 같은 크기로 다시 Configure하면 배열을 그대로 쓴다. 레벨을 다시 지을 때 GC를 건드리지 않는다.
            if (_readStrength != null && _readStrength.Length == count && _readActive != null &&
                _readActive.Capacity >= _activeLimit)
            {
                return;
            }

            _readStrength = new float[count];
            _writeStrength = new float[count];
            _readOwner = new int[count];
            _writeOwner = new int[count];
            _readAge = new float[count];
            _writeAge = new float[count];
            _bestAmount = new float[count];
            _stamp = new int[count];

            // 활성 칸은 상한을 절대 넘지 않으므로 상한만큼 미리 잡아 두면 List가 다시는 늘어나지 않는다.
            // 갱신 중 할당이 생기지 않는 것은 이 한 줄에 달려 있다.
            _readActive = new List<int>(_activeLimit);
            _writeActive = new List<int>(_activeLimit);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!_configured) return;

            Gizmos.color = new Color(0.5f, 0.85f, 0.4f, 0.5f);
            Vector3 center = new Vector3(_minX + _worldBounds.z * 0.5f, transform.position.y, _minZ + _worldBounds.w * 0.5f);
            Gizmos.DrawWireCube(center, new Vector3(_worldBounds.z, 0.05f, _worldBounds.w));

            if (_readActive == null) return;

            // 활성 칸이 많으면 기즈모가 에디터를 잡아먹는다. 눈으로 모양만 보면 되니 앞쪽만 그린다.
            int shown = Mathf.Min(_readActive.Count, 1024);
            for (int i = 0; i < shown; i++)
            {
                int index = _readActive[i];
                float strength = _readStrength[index];
                if (strength < _minStrength) continue;

                int col = index % _cols;
                int row = index / _cols;
                var cellCenter = new Vector3(_minX + (col + 0.5f) * _cellSize, transform.position.y, _minZ + (row + 0.5f) * _cellSize);

                Gizmos.color = new Color(0.5f, 0.85f, 0.4f, Mathf.Clamp01(strength) * 0.6f);
                Gizmos.DrawCube(cellCenter, new Vector3(_cellSize * 0.9f, 0.02f, _cellSize * 0.9f));
            }
        }
#endif
    }
}
