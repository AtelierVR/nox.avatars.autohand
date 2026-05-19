using System;
using System.Reflection;
using Autohand;
using Nox.Avatars.Hand;
using Nox.CCK.Avatars.Hand;
using Nox.CCK.Utils;
using UnityEngine;
using HandType = Nox.Avatars.Hand.HandType;
using Logger = Nox.CCK.Utils.Logger;
using AHand = Autohand.Hand;
using AFinger = Autohand.Finger;

namespace Nox.Avatars.AutoHand {
	public static class HandToAutoHand {
		public static AHand Convert(IHand hand) {
			Exception handError = null;
			if (hand == null || !hand.IsValid(out handError))
				throw new ArgumentException("Invalid hand: " + handError!.Message, nameof(hand));

			var anchorGo  = hand.Anchor.gameObject;
			var wasActive = anchorGo.activeSelf;
			// Deactivate so all added components have their Awake() deferred until every field is set.
			anchorGo.SetActive(false);

			// Rigidbody is required by HandBase.Awake(). Adding it explicitly before AHand ensures
			// it exists even before RequireComponent processing runs.
			var rb = anchorGo.GetOrAddComponent<Rigidbody>();
			rb.isKinematic = true;
			rb.useGravity  = false;
			var ah = anchorGo.GetOrAddComponent<AHand>();

			ah.left = hand.Type == HandType.Left;

			ah.palmTransform = hand.Palm;
			if (!ah.palmTransform) {
				var palm = new GameObject("Palm").transform;
				palm.SetParent(hand.Anchor);
				palm.position    = hand.Anchor.position;
				palm.rotation    = hand.Anchor.rotation * Quaternion.Euler(180, 0, 0);
				ah.palmTransform = palm;
				Logger.LogWarning("Hand has no palm transform. Created one at the anchor position.", hand.Anchor);
			}

			// Finger bones are children of anchor (inactive hierarchy) – their Awakes are deferred too.
			ah.fingers = new AFinger[ hand.Fingers.Length ];
			for (var i = 0; i < hand.Fingers.Length; i++) {
				ah.fingers[i]      = Convert(hand.Fingers[i]);
				ah.fingers[i].hand = ah;
			}

			// Add physics colliders sized to the actual hand geometry while the hierarchy is still
			// inactive so that HandBase.OnEnable → SetHandCollidersRecursive picks them all up.
			SetupColliders(hand);

			// Restore active state – all deferred Awakes now fire with fields properly initialized.
			anchorGo.transform.SetLayer("Hand", true); // Ensure the hand and all its children are on the correct layer for interaction. This can be overridden later if needed.

			anchorGo.SetActive(wasActive);

			return ah;
		}

		public static AFinger Convert(IFinger finger) {
			Exception fingerError = null;
			if (finger == null || !finger.IsValid(out fingerError))
				throw new ArgumentException("Invalid finger: " + fingerError!.Message, nameof(finger));

			var af = finger.Proximal.GetOrAddComponent<AFinger>();
			af.fingerType = finger.Type switch {
				FingerType.Thumb  => FingerEnum.thumb,
				FingerType.Index  => FingerEnum.index,
				FingerType.Middle => FingerEnum.middle,
				FingerType.Ring   => FingerEnum.ring,
				FingerType.Pinky  => FingerEnum.pinky,
				_                 => throw new ArgumentOutOfRangeException(nameof(finger.Type), "Unknown finger type.")
			};

			af.poseData = new FingerPoseData[ Enum.GetValues(typeof(FingerCurl)).Length - 1 ]; // without FingerCurl.TPose.
			// Pre-fill every slot with non-null arrays: HandAnimator.Start() calls CopyFromData which
			// does poseRelativeMatrix.CopyTo(...) – a null array there throws NullReferenceException.
			for (var i = 0; i < af.poseData.Length; i++)
				af.poseData[i] = new FingerPoseData { poseRelativeMatrix = new Matrix4x4[ 4 ], localRotations = new Quaternion[ 4 ] };
			for (var i = 0; i < af.poseData.Length; i++) {
				var c = finger.Poses[i].Curl;
				if (c == FingerCurl.TPose)
					continue;

				var ai = finger.Poses[i].Curl.ToAuto();
				af.poseData[(int)ai] = new FingerPoseData {
					poseRelativeMatrix = new Matrix4x4[ 4 ],
					localRotations     = finger.Poses[i].Values
				};
			}

			af.knuckleJoint = finger.Proximal;
			af.middleJoint  = finger.Intermediate;
			af.distalJoint  = finger.Distal;

			af.tip       = finger.Tip;
			af.tipRadius = finger.TipRadius;
			if (af.tipRadius == 0f) {
				af.tipRadius = 0.01f; // Default to a small radius to avoid issues with UI interaction.
				Logger.LogWarning("Tip radius is 0. Defaulting to 0.01 to avoid issues with UI interaction.", finger.Tip);
			}

			// The deprecated grip-pose arrays are declared 'internal' in AutoHand and are never
			// initialized on a runtime-created Finger component.  Finger.isDataDepricated accesses
			// .Length on them unconditionally, so they must be non-null before Awake() fires.
			var                ft = typeof(AFinger);
			const BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic;
			ft.GetField("minGripRotPose", bf)?.SetValue(af, Array.Empty<Quaternion>());
			ft.GetField("minGripPosPose", bf)?.SetValue(af, Array.Empty<Vector3>());
			ft.GetField("maxGripRotPose", bf)?.SetValue(af, Array.Empty<Quaternion>());
			ft.GetField("maxGripPosPose", bf)?.SetValue(af, Array.Empty<Vector3>());

			return af;
		}

		public static FingerPoseEnum ToAuto(this FingerCurl curl)
			=> curl switch {
				FingerCurl.Opened        => FingerPoseEnum.Open,
				FingerCurl.Closed        => FingerPoseEnum.Closed,
				FingerCurl.PinchedOpened => FingerPoseEnum.PinchOpen,
				FingerCurl.PinchedClosed => FingerPoseEnum.PinchClosed,
				_                        => throw new ArgumentOutOfRangeException(nameof(curl), "Unknown finger curl.")
			};

		// ── Collider setup ─────────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Adds physics colliders to the hand anchor and each finger bone if none exist yet.
		/// Called while the anchor GameObject is inactive so that HandBase.OnEnable picks up all
		/// colliders via SetHandCollidersRecursive when the hierarchy is later reactivated.
		/// </summary>
		private static void SetupColliders(IHand hand) {
			SetupPalmCollider(hand);
			foreach (var finger in hand.Fingers)
				SetupFingerColliders(finger);
		}

		/// <summary>
		/// Adds a BoxCollider to the hand anchor sized to enclose the proximal finger joints.
		/// The thinnest axis of the bounding box is padded to a minimum palm-depth estimate.
		/// </summary>
		private static void SetupPalmCollider(IHand hand) {
			var anchorGo = hand.Anchor.gameObject;
			if (anchorGo.HasComponent<Collider>())
				return;

			var box = anchorGo.AddComponent<BoxCollider>();

			if (hand.Fingers.Length == 0) {
				box.size   = new Vector3(0.08f, 0.02f, 0.08f);
				box.center = Vector3.zero;
				return;
			}

			// Compute bounding box of all proximal joints in anchor local space.
			var   bounds       = new Bounds(Vector3.zero, Vector3.zero);
			float avgTipRadius = 0f;
			int   count        = 0;
			foreach (var f in hand.Fingers) {
				if (f.Proximal == null)
					continue;
				bounds.Encapsulate(hand.Anchor.InverseTransformPoint(f.Proximal.position));
				avgTipRadius += f.TipRadius;
				count++;
			}
			if (count > 0)
				avgTipRadius /= count;

			// Enforce a minimum thickness on the thinnest axis (palm normal / front-back dimension).
			float thickness = Mathf.Max(avgTipRadius * 3f, 0.015f);
			var   size      = bounds.size;
			size.x = Mathf.Max(size.x, 0.02f);
			size.y = Mathf.Max(size.y, 0.02f);
			size.z = Mathf.Max(size.z, 0.02f);
			int thinAxis = (size.x <= size.y && size.x <= size.z) ? 0 : (size.y <= size.z ? 1 : 2);
			size[thinAxis] = Mathf.Max(size[thinAxis], thickness);

			box.center = bounds.center;
			box.size   = size;
		}

		/// <summary>
		/// Adds a CapsuleCollider to each phalanx bone and a SphereCollider at the finger tip.
		/// Skips bones that already have a Collider component.
		/// </summary>
		private static void SetupFingerColliders(IFinger finger) {
			AddBoneCapsule(finger.Proximal, finger.Intermediate, finger.TipRadius);
			AddBoneCapsule(finger.Intermediate, finger.Distal, finger.TipRadius);
			AddBoneCapsule(finger.Distal, finger.Tip, finger.TipRadius);

			if (finger.Tip != null && !finger.Tip.HasComponent<Collider>()) {
				var sphere = finger.Tip.gameObject.AddComponent<SphereCollider>();
				sphere.radius = Mathf.Max(finger.TipRadius, 0.005f);
			}
		}

		/// <summary>
		/// Adds a CapsuleCollider to <paramref name="from"/>, extending toward <paramref name="to"/>.
		/// The capsule axis, height, and center are derived from the bone direction in local space.
		/// </summary>
		private static void AddBoneCapsule(Transform from, Transform to, float tipRadius) {
			if (from == null || to == null)
				return;
			if (from.HasComponent<Collider>())
				return;

			var   worldVec = to.position - from.position;
			float length   = worldVec.magnitude;
			if (length < 0.001f)
				return;

			// Determine the dominant local axis (0=X, 1=Y, 2=Z) that aligns with the bone direction.
			var localDir = from.InverseTransformDirection(worldVec / length);
			var absDir   = new Vector3(Mathf.Abs(localDir.x), Mathf.Abs(localDir.y), Mathf.Abs(localDir.z));
			int axis     = (absDir.x >= absDir.y && absDir.x >= absDir.z) ? 0 : (absDir.y >= absDir.z ? 1 : 2);

			// Use tipRadius directly so all bone segments share the same cross-section as the finger tip.
			float radius = Mathf.Max(tipRadius, 0.005f);

			var cap = from.gameObject.AddComponent<CapsuleCollider>();
			cap.direction = axis;
			cap.radius    = radius;
			// Shorten by one radius so the capsule's far-end pole stops at (to - radius),
			// letting the adjacent sphere or next capsule's start hemisphere seamlessly meet it.
			cap.height = Mathf.Max(length - radius, radius * 2f);

			var center = Vector3.zero;
			center[axis] = cap.height * 0.5f * Mathf.Sign(localDir[axis]);
			cap.center   = center;
		}
	}
}