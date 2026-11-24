using System.Collections.Generic;
using UnityEngine;

namespace StandsOfValheim
{
    /// <summary>
    /// Controls Aurvandil's Fylgja's movement, idle hovering over the player's shoulder,
    /// defensive shield positioning when blocking, single-punch strikes, and the rapid ORA punch barrage.
    /// Faces the direction the player is facing (independent of camera).
    /// </summary>
    internal class StandFollower : MonoBehaviour
    {
        internal enum AttackStartStatus
        {
            Started,
            Busy,
            Unavailable
        }

        private enum AttackMode
        {
            None,
            SinglePunch,
            Barrage
        }

        public Player Owner;

        // Limb / bone transforms
        public Transform FistL;
        public Transform FistR;
        public Transform ArmL;
        public Transform ArmR;
        public Transform Torso;
        public Transform Head;
        public Transform Hips;
        public Transform UpperLegL;
        public Transform LowerLegL;
        public Transform FootL;
        public Transform UpperLegR;
        public Transform LowerLegR;
        public Transform FootR;

        public Animator StandAnimator;
        public Light Aura;
        public AudioSource StandAudio;

        // Positioning offsets relative to player's body orientation (elevated for clear mid-air levitation)
        public Vector3 IdleOffset   = new Vector3(0.58f, 0.62f, -0.78f); // levitating over right shoulder
        public Vector3 AttackOffset = new Vector3(0.35f, 0.42f, 0.58f);  // lunging forward in mid-air beside player
        public Vector3 BlockOffset  = new Vector3(0.00f, 0.46f, 0.88f);  // hovering right in front of player as astral shield
        public float Smooth = 10.5f;

        // Floating levitation & spectral breath state
        private float _floatTime = 0f;
        private float _floatLegWeight = 1f;
        private Vector3 _lastOwnerPos;
        private bool _hasLastOwnerPos = false;
        private Vector3 _smoothedLocalVel = Vector3.zero;
        private readonly List<Material> _spectralMaterials = new List<Material>();
        private readonly List<Color> _spectralBaseColors = new List<Color>();

        public void RegisterSpectralMaterial(Material mat)
        {
            if (mat == null || _spectralMaterials.Contains(mat)) return;
            _spectralMaterials.Add(mat);
            Color baseCol = mat.HasProperty("_Color") ? mat.GetColor("_Color") : mat.color;
            _spectralBaseColors.Add(baseCol);
        }

        // Full scale on summon (no slow wakeup or scale-in delay)
        private readonly Vector3 _targetScale = new Vector3(1.22f, 1.22f, 1.22f);
        private int[] _idleStateHashes;
        private bool _isPlayingIntroGPower = false;
        private float _introGPowerTimer = 0f;

        // Shared Unarmed skill factor floor so Deep North players without leveled Unarmed still hit reliably (scales up to 1.00 at Unarmed 100)
        private const float MinUnarmedSkillFactor = 0.55f;

        // Barrage tuning (Hit-limited: 12 rapid punches per trigger, 35 blunt base * BarrageDamageMultiplier scaled by Unarmed skill & buffs, 0.75x/1.5x stagger, half Demolisher knockback)
        public const int MaxPunchesPerBarrage = 12;
        private const float BarrageBaseBluntDamage = 35f;
        private const float BarrageStaminaCost = 16f;
        private const float BarrageDuration = 1.48f;
        private const float BarrageFirstHitDelay = 0.03f;
        private const float BarrageHitInterval = 0.115f;
        private const float BarrageAnimInterval = 0.07f;
        private const float BarrageAnimSpeed = 6.2f;
        private const float BarrageAnimStartNormalized = 0.22f;
        private const float BarrageVfxInterval = 0.025f;
        private const float DefaultBarrageCooldown = 6.0f;
        private const float BarrageHitStaggerMultiplier = 0.75f;
        private const float BarrageFinisherStaggerMultiplier = 1.50f;

        // Single-punch tuning (1 hit, fast left-click trigger, 120 blunt default base scaled by Unarmed skill & buffs, 1.5x 3rd-hit combo finisher)
        private const float DefaultSinglePunchBaseBluntDamage = 120f;
        private const float SinglePunchFinisherDamageMultiplier = 1.5f;
        private const float SinglePunchHitPushForce = 18f;
        private const float SinglePunchFinisherPushForce = 30f;
        private const float SinglePunchHitStaggerMultiplier = 1.0f;
        private const float SinglePunchFinisherStaggerMultiplier = 1.5f;
        private const float SinglePunchStaminaCost = 5f;
        private const float SinglePunchDuration = 0.34f;
        private const float SinglePunchHitDelay = 0.10f;
        private const float SinglePunchAnimSpeed = 2.0f;
        private const float SinglePunchCooldown = 0.06f;
        private const float SingleComboResetWindow = 0.90f;

        // Attack state
        public bool IsAttacking { get; private set; }
        private AttackMode _attackMode = AttackMode.None;
        private int _punchesRemaining = 0;
        private float _attackTimer = 0f;
        private float _punchAnimTime = 0f;
        private float _nextPunchDamageTime = 0f;
        private float _nextPunchAnimTriggerTime = 0f;
        private float _nextBarrageVfxTime = 0f;
        private float _singleCooldownTimer = 0f;
        private float _barrageCooldownTimer = 0f;
        private StandBarrageVfx _barrageVfx;

        // Cached half-Demolisher knockback tuning
        private static bool s_resolvedDemolisherStats = false;
        private static float s_barrageHitPushForce = 2.5f;
        private static float s_barrageFinisherPushForce = 16f;

        // Player unarmed animation parameters
        private string _punchAnimBase = "unarmed_attack";
        private int _punchChainLevels = 3;
        private int _barragePunchIndex = 0;
        private int _singleComboIndex = 0;
        private int _activeSingleComboIndex = 0;
        private float _lastSinglePunchTime = -10f;
        private string _lastTrigger = null;

        // Block reaction state
        private float _blockReactionTimer = 0f;
        private bool _isParryReaction = false;
        private float _guardBlend = 0f;

        // Animator parameter cache
        private int _blockingParamHash = 0;
        private bool _hasBlockingParam = false;
        private readonly HashSet<string> _animParamNames = new HashSet<string>();

        internal void ConfigureAnimatorStates(int[] idleStateHashes)
        {
            _idleStateHashes = idleStateHashes;
        }

        internal void NotifyIntroGPowerStarted()
        {
            _isPlayingIntroGPower = true;
            _introGPowerTimer = 1.6f;
        }

        private bool IsBlockingNow() =>
            !IsAttacking && Owner != null && Owner.IsBlocking() && StandManager.AreHandsFree(Owner);

        private void InterruptIntroGPower(bool resetBaseLayer)
        {
            if (!_isPlayingIntroGPower) return;
            _isPlayingIntroGPower = false;
            _introGPowerTimer = 0f;

            if (StandAnimator == null) return;
            if (HasParameter("gpower"))
            {
                StandAnimator.ResetTrigger("gpower");
            }

            if (_idleStateHashes == null) return;
            int startLayer = resetBaseLayer ? 0 : 1;
            int count = Mathf.Min(_idleStateHashes.Length, StandAnimator.layerCount);
            for (int layer = startLayer; layer < count; layer++)
            {
                int idleHash = _idleStateHashes[layer];
                if (idleHash == 0) continue;

                var cur = StandAnimator.GetCurrentAnimatorStateInfo(layer);
                if (cur.fullPathHash != idleHash || StandAnimator.IsInTransition(layer))
                {
                    StandAnimator.CrossFadeInFixedTime(idleHash, 0.04f, layer, 0f);
                }
            }
        }

        private bool AreAllLayersAtIdle()
        {
            if (StandAnimator == null || _idleStateHashes == null) return true;
            int count = Mathf.Min(_idleStateHashes.Length, StandAnimator.layerCount);
            for (int layer = 0; layer < count; layer++)
            {
                int idleHash = _idleStateHashes[layer];
                if (idleHash == 0) continue;
                if (StandAnimator.IsInTransition(layer)) return false;
                if (StandAnimator.GetCurrentAnimatorStateInfo(layer).fullPathHash != idleHash) return false;
            }
            return true;
        }

        private void Start()
        {
            transform.localScale = _targetScale;
            _barrageVfx = new StandBarrageVfx();

            // Detect player's unarmed attack animation configuration
            if (Owner != null && Owner.m_unarmedWeapon != null)
            {
                var shared = Owner.m_unarmedWeapon.m_itemData?.m_shared;
                if (shared != null && shared.m_attack != null && !string.IsNullOrEmpty(shared.m_attack.m_attackAnimation))
                {
                    _punchAnimBase = shared.m_attack.m_attackAnimation;
                    _punchChainLevels = shared.m_attack.m_attackChainLevels;
                    Plugin.Log?.LogInfo($"StandFollower: Using player's punch animation '{_punchAnimBase}' (chains: {_punchChainLevels})");
                }
            }

            if (StandAnimator != null)
            {
                foreach (var p in StandAnimator.parameters)
                {
                    _animParamNames.Add(p.name);
                    if (p.name == "blocking")
                    {
                        _hasBlockingParam = true;
                        _blockingParamHash = p.nameHash;
                    }
                }
            }
        }

        private void Update()
        {
            if (Owner == null || Owner.IsDead())
            {
                Destroy(gameObject);
                return;
            }

            _floatTime += Time.deltaTime;
            UpdateOwnerVelocityTracking(Time.deltaTime);
            UpdateSpectralShimmer();

            if (_singleCooldownTimer > 0f) _singleCooldownTimer -= Time.deltaTime;
            if (_barrageCooldownTimer > 0f) _barrageCooldownTimer -= Time.deltaTime;
            if (_blockReactionTimer > 0f) _blockReactionTimer -= Time.deltaTime;
            _barrageVfx?.Tick(Time.deltaTime);

            if (_isPlayingIntroGPower)
            {
                _introGPowerTimer -= Time.deltaTime;
                if (_introGPowerTimer <= 0f || (_introGPowerTimer < 1.35f && AreAllLayersAtIdle()))
                {
                    _isPlayingIntroGPower = false;
                }
            }

            bool isBlocking = IsBlockingNow();
            if (isBlocking && _isPlayingIntroGPower)
            {
                InterruptIntroGPower(resetBaseLayer: true);
            }

            // Sync blocking parameter before Unity evaluates Animator this frame
            if (_hasBlockingParam && StandAnimator != null)
            {
                StandAnimator.SetBool(_blockingParamHash, isBlocking);
            }

            if (IsAttacking)
            {
                if (!StandManager.AreHandsFree(Owner))
                {
                    StopAttack();
                    UpdateFollow();
                }
                else
                {
                    UpdateAttack();
                }
            }
            else if (isBlocking)
            {
                UpdateBlockDefend();
            }
            else
            {
                UpdateFollow();
            }
        }

        private void LateUpdate()
        {
            bool isBlocking = IsBlockingNow();

            _guardBlend = Mathf.MoveTowards(_guardBlend, isBlocking ? 1f : 0f, Time.deltaTime * 8f);

            float targetFloatLegWeight = IsAttacking ? 0.45f : (isBlocking ? 0.65f : 1.0f);
            _floatLegWeight = Mathf.MoveTowards(_floatLegWeight, targetFloatLegWeight, Time.deltaTime * 5f);

            // Apply weightless mid-air levitation leg posture so feet dangle off the ground instead of standing flat
            ApplyFloatingLegPose(_floatLegWeight);

            // Defensive and flurry punch bone posing
            if (IsAttacking && _attackMode == AttackMode.Barrage)
            {
                // Subtle high-frequency alternating forearm drive in sync with 6.2x punch clips
                float waveL = Mathf.Max(0f, Mathf.Sin(_punchAnimTime * 54f));
                float waveR = Mathf.Max(0f, Mathf.Sin(_punchAnimTime * 54f + Mathf.PI));
                if (ArmL != null) ArmL.position += transform.forward * (waveL * 0.085f);
                if (ArmR != null) ArmR.position += transform.forward * (waveR * 0.085f);
            }
            else if (isBlocking)
            {
                if (_blockReactionTimer > 0f)
                {
                    if (_isParryReaction)
                    {
                        // Deflection thrust forward
                        if (FistL != null) FistL.position += transform.forward * 0.40f;
                        if (FistR != null) FistR.position += transform.forward * 0.40f;
                    }
                    else
                    {
                        // Absorb recoil backward
                        if (FistL != null) FistL.position -= transform.forward * 0.12f;
                        if (FistR != null) FistR.position -= transform.forward * 0.12f;
                    }
                }
                else
                {
                    // Guard stance: cross arms across chest (eased in so it never pops)
                    if (FistL != null) FistL.position += (transform.right * 0.15f + transform.forward * 0.16f + transform.up * 0.10f) * _guardBlend;
                    if (FistR != null) FistR.position += (-transform.right * 0.15f + transform.forward * 0.16f + transform.up * 0.10f) * _guardBlend;
                }
            }
        }

        public AttackStartStatus TryStartAttack(bool allowStaminaFlash = true) =>
            TryBeginAttack(AttackMode.Barrage, allowStaminaFlash);

        public AttackStartStatus TryStartSinglePunch(bool allowStaminaFlash = true) =>
            TryBeginAttack(AttackMode.SinglePunch, allowStaminaFlash);

        private AttackStartStatus TryBeginAttack(AttackMode mode, bool allowStaminaFlash)
        {
            if (Owner == null || !StandManager.AreHandsFree(Owner))
            {
                return AttackStartStatus.Unavailable;
            }

            if (mode == AttackMode.SinglePunch)
            {
                if (IsAttacking || _singleCooldownTimer > 0f) return AttackStartStatus.Busy;
            }
            else if (mode == AttackMode.Barrage)
            {
                if (IsAttacking && _attackMode == AttackMode.Barrage) return AttackStartStatus.Busy;
                if (_barrageCooldownTimer > 0f)
                {
                    if (allowStaminaFlash && MessageHud.instance != null)
                    {
                        MessageHud.instance.ShowMessage(
                            MessageHud.MessageType.TopLeft,
                            $"Aurvandil's Fylgja: Barrage on cooldown ({_barrageCooldownTimer:0.0}s)");
                    }
                    return AttackStartStatus.Unavailable;
                }
            }

            float staminaCost = (mode == AttackMode.Barrage) ? BarrageStaminaCost : SinglePunchStaminaCost;
            if (!Owner.HaveStamina(staminaCost))
            {
                if (allowStaminaFlash && Hud.instance != null)
                {
                    Hud.instance.StaminaBarEmptyFlash();
                }
                return AttackStartStatus.Unavailable;
            }

            // Allow an incoming barrage to cancel out of a completed/recovering single punch cleanly
            if (IsAttacking && _attackMode == AttackMode.SinglePunch && mode == AttackMode.Barrage)
            {
                StopAttack();
            }

            Owner.UseStamina(staminaCost);

            // Cancel the spawn Guardian Power pose cleanly if the player attacks right away
            InterruptIntroGPower(resetBaseLayer: false);

            IsAttacking = true;
            _attackMode = mode;
            _punchAnimTime = 0f;
            _lastTrigger = null;

            if (mode == AttackMode.Barrage)
            {
                _punchesRemaining = MaxPunchesPerBarrage;
                _attackTimer = BarrageDuration;
                _barragePunchIndex = 1; // 0 triggered immediately below
                _nextPunchDamageTime = Time.time + BarrageFirstHitDelay;
                _nextPunchAnimTriggerTime = BarrageAnimInterval;
                _nextBarrageVfxTime = 0f;

                TriggerPunchAnim(BarrageAnimSpeed, 0, BarrageAnimStartNormalized, 0.018f);

                if (StandAudio != null && SoundManager.OraClip != null)
                {
                    StandAudio.clip = SoundManager.OraClip;
                    StandAudio.time = 0f;
                    StandAudio.loop = true;
                    StandAudio.Play();
                }

                if (MessageHud.instance != null)
                {
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "★ AURVANDIL'S FYLGJA: ORA ORA ORA! ★");
                }

                Plugin.Log?.LogInfo($"Aurvandil's Fylgja: 12-hit ORA barrage activated ({BarrageBaseBluntDamage * Plugin.BarrageDamageMultiplier:0.#} base blunt per hit, {Plugin.BarrageDamageMultiplier:0.##}x multiplier)!");
            }
            else
            {
                _punchesRemaining = 1;
                _attackTimer = SinglePunchDuration;
                _nextPunchDamageTime = Time.time + SinglePunchHitDelay;

                if (Time.time - _lastSinglePunchTime > SingleComboResetWindow)
                {
                    _singleComboIndex = 0;
                }

                int comboIdx = _singleComboIndex;
                _activeSingleComboIndex = comboIdx;
                _singleComboIndex = (_singleComboIndex + 1) % Mathf.Max(1, _punchChainLevels);

                TriggerPunchAnim(SinglePunchAnimSpeed, comboIdx);

                // Ensure no ORA shout plays for single punches
                if (StandAudio != null && StandAudio.isPlaying)
                {
                    StandAudio.Stop();
                }
            }

            return AttackStartStatus.Started;
        }

        private void TriggerPunchAnim(float speed, int chainIdx, float startNormalizedTime = 0f, float crossFadeDuration = 0.04f)
        {
            if (StandAnimator == null) return;

            StandAnimator.speed = speed;

            // Ensure unarmed animation state (0 = Unarmed)
            if (HasParameter("statef")) StandAnimator.SetFloat("statef", 0f);
            if (HasParameter("statei")) StandAnimator.SetInteger("statei", 0);

            // Determine punch trigger based on player's unarmed attack
            string baseAnim = !string.IsNullOrEmpty(_punchAnimBase) ? _punchAnimBase : "unarmed_attack";
            string triggerName = (_punchChainLevels > 1) ? (baseAnim + chainIdx) : baseAnim;

            // Reset previous trigger if any to allow rapid retriggering
            if (!string.IsNullOrEmpty(_lastTrigger) && HasParameter(_lastTrigger))
            {
                StandAnimator.ResetTrigger(_lastTrigger);
            }

            // Set new trigger
            if (HasParameter(triggerName))
            {
                StandAnimator.SetTrigger(triggerName);
                _lastTrigger = triggerName;
            }
            else if (HasParameter(baseAnim))
            {
                StandAnimator.SetTrigger(baseAnim);
                _lastTrigger = baseAnim;
            }

            // Crossfade into state directly if present in Animator for instantaneous punch snap
            int stateHash = Animator.StringToHash(triggerName);
            if (StandAnimator.HasState(0, stateHash))
            {
                StandAnimator.CrossFadeInFixedTime(stateHash, crossFadeDuration, 0, startNormalizedTime);
            }
            else
            {
                int baseHash = Animator.StringToHash(baseAnim);
                if (StandAnimator.HasState(0, baseHash))
                {
                    StandAnimator.CrossFadeInFixedTime(baseHash, crossFadeDuration, 0, startNormalizedTime);
                }
            }
        }

        private bool HasParameter(string paramName) =>
            !string.IsNullOrEmpty(paramName) && _animParamNames.Contains(paramName);

        public void OnBlockHit(bool isParry)
        {
            _blockReactionTimer = 0.25f;
            _isParryReaction = isParry;
        }

        private void UpdateBlockDefend()
        {
            var t = Owner.transform;

            // Fylgja hovers smoothly right in front of the player with subtle buoyant levitation
            float bobY = Mathf.Sin(_floatTime * 2.4f) * 0.065f;
            Vector3 targetPos = t.TransformPoint(BlockOffset) + Vector3.up * bobY;
            transform.position = Vector3.Lerp(transform.position, targetPos, Smooth * 1.6f * Time.deltaTime);

            // Fylgja faces the EXACT direction the player is facing with a slight braced aerial lean
            Quaternion guardTilt = Quaternion.Euler(-3.5f + Mathf.Sin(_floatTime * 1.9f) * 1.5f, 0f, Mathf.Cos(_floatTime * 1.4f) * 1.5f);
            transform.rotation = Quaternion.Slerp(transform.rotation, t.rotation * guardTilt, Smooth * 1.6f * Time.deltaTime);

            // Flare Fylgja aura into a bright protective barrier
            if (Aura != null)
            {
                Aura.intensity = Mathf.Lerp(Aura.intensity, 2.5f + Mathf.Sin(_floatTime * 3.0f) * 0.25f, Smooth * Time.deltaTime);
            }
        }

        private void UpdateAttack()
        {
            _attackTimer -= Time.deltaTime;
            _punchAnimTime += Time.deltaTime;

            // Move Fylgja forward into levitating attack position
            var t = Owner.transform;
            float hoverY = Mathf.Sin(_floatTime * 2.6f) * 0.055f;
            Vector3 targetPos = t.TransformPoint(AttackOffset) + Vector3.up * hoverY;
            transform.position = Vector3.Lerp(transform.position, targetPos, Smooth * 1.6f * Time.deltaTime);

            // Fylgja faces the direction the player is facing with an aggressive forward aerial lean
            Quaternion attackTilt = Quaternion.Euler(5.5f, 0f, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, t.rotation * attackTilt, Smooth * 1.6f * Time.deltaTime);

            if (_attackMode == AttackMode.Barrage)
            {
                // Subtle flurry recoil forward with punches
                transform.position += transform.forward * (Mathf.Abs(Mathf.Sin(_punchAnimTime * 42f)) * 0.06f);

                // Flare Fylgja aura during barrage
                if (Aura != null)
                {
                    float pulse = 2.8f + Mathf.Abs(Mathf.Sin(_punchAnimTime * 40f)) * 0.65f;
                    Aura.intensity = Mathf.Lerp(Aura.intensity, pulse, Smooth * Time.deltaTime);
                    Aura.range = Mathf.Lerp(Aura.range, 5.4f, Smooth * Time.deltaTime);
                }

                // Emit high-frequency spectral afterimage punches forward
                if (_punchAnimTime >= _nextBarrageVfxTime)
                {
                    _nextBarrageVfxTime = _punchAnimTime + BarrageVfxInterval;
                    _barrageVfx?.SpawnFlurryFist(transform);
                }

                // Cycle valid unarmed punch chains rapidly (~14 punches/sec), skipping slow wind-up
                if (_punchAnimTime >= _nextPunchAnimTriggerTime)
                {
                    _nextPunchAnimTriggerTime = _punchAnimTime + BarrageAnimInterval;
                    int chainIdx = _barragePunchIndex % Mathf.Max(1, _punchChainLevels);
                    _barragePunchIndex++;
                    TriggerPunchAnim(BarrageAnimSpeed, chainIdx, BarrageAnimStartNormalized, 0.018f);
                }

                // Punch damage tick every ~0.115s, strictly limited to 12 hits per trigger (~1.30s total)
                if (Time.time >= _nextPunchDamageTime && _punchesRemaining > 0)
                {
                    _nextPunchDamageTime = Time.time + BarrageHitInterval;
                    bool isFinisher = _punchesRemaining == 1;
                    _punchesRemaining--;

                    EnsureDemolisherHalfStatsResolved();

                    HitData.DamageTypes dynamicDmg = ComputeDynamicStandPunchDamage(
                        BarrageBaseBluntDamage,
                        Plugin.BarrageDamageMultiplier,
                        out Skills.SkillType skillType);
                    float staggerMult = isFinisher ? BarrageFinisherStaggerMultiplier : BarrageHitStaggerMultiplier;

                    PlayNormalPunchSwingSound();

                    PerformPunchHit(
                        damage: dynamicDmg,
                        skillType: skillType,
                        pushForce: isFinisher ? s_barrageFinisherPushForce : s_barrageHitPushForce,
                        staggerMultiplier: staggerMult,
                        skillRaiseAmount: 0.20f);

                    Vector3 ringPos = transform.position + transform.up * 1.32f + transform.forward * 1.30f;
                    _barrageVfx?.SpawnShockRing(ringPos, transform.forward, isFinisher ? 1.55f : 1.0f);

                    if (_punchesRemaining <= 0)
                    {
                        StopAttack();
                        return;
                    }
                }
            }
            else if (_attackMode == AttackMode.SinglePunch)
            {
                // Single punch connects once on extension, then lets follow-through finish cleanly
                if (Time.time >= _nextPunchDamageTime && _punchesRemaining > 0)
                {
                    _punchesRemaining--;
                    bool isComboFinisher = _punchChainLevels > 1 && _activeSingleComboIndex == _punchChainLevels - 1;
                    float singleBase = Plugin.SinglePunchBaseDamage > 0f
                        ? Plugin.SinglePunchBaseDamage
                        : DefaultSinglePunchBaseBluntDamage;
                    float comboMult = isComboFinisher ? SinglePunchFinisherDamageMultiplier : 1.0f;

                    HitData.DamageTypes singleDmg = ComputeDynamicStandPunchDamage(
                        singleBase,
                        comboMult,
                        out Skills.SkillType skillType);

                    PlayNormalPunchSwingSound();
                    PerformPunchHit(
                        damage: singleDmg,
                        skillType: skillType,
                        pushForce: isComboFinisher ? SinglePunchFinisherPushForce : SinglePunchHitPushForce,
                        staggerMultiplier: isComboFinisher ? SinglePunchFinisherStaggerMultiplier : SinglePunchHitStaggerMultiplier,
                        skillRaiseAmount: 1.0f);

                    if (isComboFinisher)
                    {
                        Vector3 ringPos = transform.position + transform.up * 1.32f + transform.forward * 1.25f;
                        _barrageVfx?.SpawnShockRing(ringPos, transform.forward, 1.15f);
                    }
                }
            }

            // Completion / safety timeout
            if (_attackTimer <= 0f)
            {
                StopAttack();
            }
        }

        private void StopAttack()
        {
            if (_attackMode == AttackMode.SinglePunch)
            {
                _lastSinglePunchTime = Time.time;
                _singleCooldownTimer = SinglePunchCooldown;
            }
            else if (_attackMode == AttackMode.Barrage)
            {
                _barrageCooldownTimer = Plugin.BarrageCooldownSeconds > 0f
                    ? Plugin.BarrageCooldownSeconds
                    : DefaultBarrageCooldown;
            }

            _attackMode = AttackMode.None;
            IsAttacking = false;
            _punchesRemaining = 0;

            if (StandAnimator != null)
            {
                StandAnimator.speed = 1.0f;
                if (!string.IsNullOrEmpty(_lastTrigger) && HasParameter(_lastTrigger))
                {
                    StandAnimator.ResetTrigger(_lastTrigger);
                }
                _lastTrigger = null;

                // Cleanly return to idle pose instead of lingering in a slowed mid-punch frame
                if (_idleStateHashes != null && _idleStateHashes.Length > 0 && _idleStateHashes[0] != 0)
                {
                    StandAnimator.CrossFadeInFixedTime(_idleStateHashes[0], 0.08f, 0, 0f);
                }
            }

            // Stop barrage sound
            if (StandAudio != null && StandAudio.isPlaying)
            {
                StandAudio.Stop();
            }
        }

        private void OnDestroy()
        {
            if (StandAudio != null && StandAudio.isPlaying)
            {
                StandAudio.Stop();
            }
            _barrageVfx?.Dispose();

            for (int i = 0; i < _spectralMaterials.Count; i++)
            {
                if (_spectralMaterials[i] != null)
                {
                    Destroy(_spectralMaterials[i]);
                }
            }
            _spectralMaterials.Clear();
            _spectralBaseColors.Clear();
        }

        private void UpdateOwnerVelocityTracking(float dt)
        {
            if (Owner == null || dt <= 0.0001f) return;

            Vector3 ownerPos = Owner.transform.position;
            if (!_hasLastOwnerPos)
            {
                _lastOwnerPos = ownerPos;
                _hasLastOwnerPos = true;
                return;
            }

            Vector3 worldVel = (ownerPos - _lastOwnerPos) / dt;
            _lastOwnerPos = ownerPos;
            worldVel.y = 0f;
            if (worldVel.sqrMagnitude > 225f) worldVel = Vector3.zero; // ignore teleports

            Vector3 localVel = Owner.transform.InverseTransformDirection(worldVel);
            _smoothedLocalVel = Vector3.Lerp(_smoothedLocalVel, localVel, Mathf.Clamp01(dt * 6f));
        }

        private void UpdateSpectralShimmer()
        {
            float baseOpacity = Plugin.ModelOpacity;
            if (baseOpacity >= 0.99f || _spectralMaterials.Count == 0) return;

            // Subtle living astral breath shimmer synced with levitation phase
            float breath = Mathf.Sin(_floatTime * 2.15f) * 0.035f + Mathf.Cos(_floatTime * 0.95f) * 0.015f;
            float liveAlpha = Mathf.Clamp(baseOpacity + breath, 0.18f, 0.96f);

            for (int i = 0; i < _spectralMaterials.Count; i++)
            {
                Material mat = _spectralMaterials[i];
                if (mat == null) continue;
                Color c = _spectralBaseColors[i];
                c.a = liveAlpha;
                mat.color = c;
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            }
        }

        private void ApplyFloatingLegPose(float weight)
        {
            if (weight <= 0.001f) return;

            Vector3 rightAxis = transform.right;
            // Subtle vertical-velocity inertia: legs gently trail downward while rising and tuck slightly while descending
            float hoverVelSway = Mathf.Cos(_floatTime * 2.15f) * 4.5f;
            float moveTrail = Mathf.Clamp(_smoothedLocalVel.z * 2.0f, -4f, 12f);

            // Right leg: slightly raised knee in classic astral levitation stance
            RotateBoneSagittal(UpperLegR, (-20f + hoverVelSway + moveTrail * 0.7f) * weight, rightAxis);
            RotateBoneSagittal(LowerLegR, ( 34f - hoverVelSway * 0.8f) * weight, rightAxis);
            RotateBoneSagittal(FootR,     ( 28f + hoverVelSway * 0.5f) * weight, rightAxis);

            // Left leg: trailing slightly behind with relaxed knee bend and downward-pointed boot
            RotateBoneSagittal(UpperLegL, (  7f + hoverVelSway * 0.8f + moveTrail) * weight, rightAxis);
            RotateBoneSagittal(LowerLegL, ( 18f - hoverVelSway * 0.6f) * weight, rightAxis);
            RotateBoneSagittal(FootL,     ( 24f + hoverVelSway * 0.4f) * weight, rightAxis);
        }

        private static void RotateBoneSagittal(Transform bone, float angleDeg, Vector3 rightAxis)
        {
            if (bone == null || Mathf.Abs(angleDeg) < 0.01f) return;
            bone.rotation = Quaternion.AngleAxis(angleDeg, rightAxis) * bone.rotation;
        }

        private void UpdateFollow()
        {
            var t = Owner.transform;

            // Multi-harmonic levitation bobbing + subtle figure-8 astral drift around the player's shoulder
            float bobY = Mathf.Sin(_floatTime * 2.15f) * 0.14f + Mathf.Cos(_floatTime * 0.95f) * 0.045f;
            Vector3 drift = t.right   * (Mathf.Sin(_floatTime * 1.15f) * 0.055f)
                          + t.forward * (Mathf.Cos(_floatTime * 1.55f) * 0.045f);
            Vector3 target = t.TransformPoint(IdleOffset) + Vector3.up * bobY + drift;
            transform.position = Vector3.Lerp(transform.position, target, Smooth * Time.deltaTime);

            // Gentle floating pitch/roll sway + graceful aerial forward lean & bank when the player runs
            float idlePitch = Mathf.Sin(_floatTime * 1.60f) * 3.0f;
            float idleRoll  = Mathf.Cos(_floatTime * 1.25f) * 2.8f;
            float movePitch = Mathf.Clamp(_smoothedLocalVel.z * 2.1f, -6f, 14f);
            float moveRoll  = Mathf.Clamp(-_smoothedLocalVel.x * 2.2f, -10f, 10f);
            Quaternion floatTilt = Quaternion.Euler(idlePitch + movePitch, 0f, idleRoll + moveRoll);

            // Fylgja faces the direction the player is facing (independent of camera) with smooth aerial tilt
            transform.rotation = Quaternion.Slerp(transform.rotation, t.rotation * floatTilt, Smooth * Time.deltaTime);

            // Breathe aura intensity and range gently in phase with the hover cycle
            if (Aura != null)
            {
                float targetIntensity = 1.45f + Mathf.Sin(_floatTime * 2.15f) * 0.28f;
                float targetRange = 4.35f + Mathf.Sin(_floatTime * 2.15f) * 0.35f;
                Aura.intensity = Mathf.Lerp(Aura.intensity, targetIntensity, Smooth * Time.deltaTime);
                Aura.range = Mathf.Lerp(Aura.range, targetRange, Smooth * Time.deltaTime);
            }
        }

        /// <summary>
        /// Dynamically computes stand punch damage starting from <paramref name="baseBluntDamage"/>
        /// (base blunt * unarmed attack multiplier * Unarmed random skill factor [floored at 0.55] * active StatusEffects),
        /// scaled by <paramref name="multiplier"/> (e.g. 1.0x / 1.5x for single punches, 4.0x for barrage hits).
        /// </summary>
        private HitData.DamageTypes ComputeDynamicStandPunchDamage(float baseBluntDamage, float multiplier, out Skills.SkillType skillType)
        {
            skillType = Skills.SkillType.Unarmed;
            var dmg = new HitData.DamageTypes { m_blunt = Mathf.Max(1f, baseBluntDamage) };
            float attackDmgMult = 1.0f;

            var unarmedItem = Owner != null && Owner.m_unarmedWeapon != null
                ? Owner.m_unarmedWeapon.m_itemData
                : null;

            if (unarmedItem != null && unarmedItem.m_shared != null)
            {
                skillType = unarmedItem.m_shared.m_skillType;
                if (unarmedItem.m_shared.m_attack != null && unarmedItem.m_shared.m_attack.m_damageMultiplier > 0f)
                {
                    attackDmgMult = unarmedItem.m_shared.m_attack.m_damageMultiplier;
                }
            }

            float rawSkillFactor = Owner != null ? Owner.GetRandomSkillFactor(skillType) : MinUnarmedSkillFactor;
            float skillFactor = Mathf.Max(MinUnarmedSkillFactor, rawSkillFactor);
            dmg.Modify(attackDmgMult * skillFactor);

            if (Owner != null && Owner.GetSEMan() != null)
            {
                var probeHit = new HitData
                {
                    m_damage = dmg,
                    m_skill = skillType
                };
                Owner.GetSEMan().ModifyAttack(skillType, ref probeHit);
                dmg = probeHit.m_damage;
            }

            dmg.Modify(Mathf.Max(0.1f, multiplier));
            return dmg;
        }

        private void PlayNormalPunchSwingSound()
        {
            Vector3 pos = transform.position + transform.up * 1.35f + transform.forward * 0.55f;
            Quaternion rot = transform.rotation;

            var shared = Owner != null && Owner.m_unarmedWeapon != null
                ? Owner.m_unarmedWeapon.m_itemData?.m_shared
                : null;

            if (TryTriggerEffectList(shared?.m_attack?.m_triggerEffect, pos, rot)) return;
            if (TryTriggerEffectList(shared?.m_triggerEffect, pos, rot)) return;
            if (TryTriggerEffectList(shared?.m_startEffect, pos, rot)) return;

            if (ZNetScene.instance != null)
            {
                var swingPrefab = ZNetScene.instance.GetPrefab("sfx_unarmed_swing");
                if (swingPrefab != null)
                {
                    Instantiate(swingPrefab, pos, rot);
                }
            }
        }

        private void PlayNormalPunchHitSound(Vector3 hitPoint)
        {
            Quaternion rot = transform.rotation;

            var shared = Owner != null && Owner.m_unarmedWeapon != null
                ? Owner.m_unarmedWeapon.m_itemData?.m_shared
                : null;

            if (TryTriggerEffectList(shared?.m_hitEffect, hitPoint, rot)) return;
            if (TryTriggerEffectList(shared?.m_attack?.m_hitEffect, hitPoint, rot)) return;

            if (ZNetScene.instance != null)
            {
                var hitPrefab = ZNetScene.instance.GetPrefab("sfx_unarmed_hit");
                if (hitPrefab != null)
                {
                    Instantiate(hitPrefab, hitPoint, rot);
                }
            }
        }

        private static bool TryTriggerEffectList(EffectList effects, Vector3 pos, Quaternion rot)
        {
            if (effects == null || effects.m_effectPrefabs == null || effects.m_effectPrefabs.Length == 0)
            {
                return false;
            }
            effects.Create(pos, rot, null, 1f);
            return true;
        }

        private void PerformPunchHit(
            HitData.DamageTypes damage,
            Skills.SkillType skillType,
            float pushForce,
            float staggerMultiplier,
            float skillRaiseAmount)
        {
            Vector3 center = Owner.transform.position + Owner.transform.forward * 2.2f + Vector3.up * 1.1f;
            Collider[] hits = Physics.OverlapSphere(center, 2.2f);
            var damaged = new HashSet<IDestructible>();
            bool playedHitSound = false;
            bool hitLivingTarget = false;

            foreach (var col in hits)
            {
                if (col == null || col.isTrigger) continue;
                var destructible = col.GetComponentInParent<IDestructible>();
                if (destructible == null || damaged.Contains(destructible)) continue;
                if (destructible is Character c)
                {
                    if (c == Owner || c.IsDead()) continue;

                    // PvP / Friendly Fire enforcement:
                    // Other players can only be attacked when both the Fylgja's owner and the target player have PvP enabled.
                    if (c.IsPlayer())
                    {
                        if (!Owner.IsPVPEnabled() || !c.IsPVPEnabled())
                        {
                            continue;
                        }
                    }
                    // Friendly tamed creatures are also protected unless the owner has PvP (friendly fire) enabled.
                    else if (c.IsTamed() && !Owner.IsPVPEnabled())
                    {
                        continue;
                    }

                    hitLivingTarget = true;
                }

                damaged.Add(destructible);
                Vector3 hitPoint = col.bounds.ClosestPoint(center);

                var hit = new HitData
                {
                    m_point = hitPoint,
                    m_dir = Owner.transform.forward,
                    m_pushForce = pushForce,
                    m_staggerMultiplier = staggerMultiplier,
                    m_damage = damage.Clone(),
                    m_hitType = HitData.HitType.PlayerHit,
                    m_blockable = true,
                    m_dodgeable = true,
                    m_skill = skillType,
                    m_skillLevel = Owner.GetSkillLevel(skillType),
                    m_hitCollider = col
                };
                hit.SetAttacker(Owner);

                destructible.Damage(hit);

                if (!playedHitSound)
                {
                    playedHitSound = true;
                    PlayNormalPunchHitSound(hitPoint);
                }
            }

            if (hitLivingTarget && skillRaiseAmount > 0f && skillType != Skills.SkillType.None)
            {
                Owner.RaiseSkill(skillType, skillRaiseAmount);
            }
        }

        private static void EnsureDemolisherHalfStatsResolved()
        {
            if (s_resolvedDemolisherStats) return;
            if (ObjectDB.instance == null) return;

            s_resolvedDemolisherStats = true;
            try
            {
                GameObject demPrefab = ObjectDB.instance.GetItemPrefab("SledgeDemolisher");
                var shared = demPrefab != null ? demPrefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
                if (shared != null)
                {
                    float forceMult = (shared.m_attack != null && shared.m_attack.m_forceMultiplier > 0f)
                        ? shared.m_attack.m_forceMultiplier
                        : 1f;
                    float demAttackForce = Mathf.Max(20f, shared.m_attackForce * forceMult);

                    // Target: knockback effect is half of Demolisher.
                    // Keep hits 1..11 low so targets stay in range, and put the main knockback on the 12th finisher.
                    float halfDemolisherForce = 0.5f * demAttackForce;
                    s_barrageHitPushForce = Mathf.Clamp(halfDemolisherForce * 0.05f, 1.5f, 2.5f);
                    s_barrageFinisherPushForce = Mathf.Clamp(halfDemolisherForce * 0.32f, 10f, 25f);

                    Plugin.Log?.LogInfo(
                        $"StandFollower: Calibrated barrage knockback to half Demolisher " +
                        $"(Demolisher force={demAttackForce} -> hitForce={s_barrageHitPushForce:0.##}, finisherForce={s_barrageFinisherPushForce:0.##}).");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogWarning($"StandFollower: Failed to resolve SledgeDemolisher stats, using defaults: {ex.Message}");
            }
        }
    }
}
