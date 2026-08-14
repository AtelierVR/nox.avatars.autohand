using Autohand;
using Nox.CCK.Utils;
using UnityEngine;
using AHand = Autohand.Hand;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.CCK.Avatars.AutoHand {
	/// <summary>
	/// Static helpers to reliably grab and release an object with a single AutoHand <see cref="AHand"/>.
	/// </summary>
	public static class AutoHandGrab {

		/// <summary>
		/// Configures the target <see cref="GameObject"/> as a grabbable, then grabs it with the given hand.
		/// Returns true if the grab was requested successfully.
		/// </summary>
		public static bool Grab(AHand hand, GameObject target) {
			if (!CanGrab(hand))
				return false;
			if (target == null) {
				Logger.LogWarning("target is null.", tag: nameof(AutoHandGrab));
				return false;
			}

			var grabbable = EnsureGrabbable(target);
			return Grab(hand, grabbable);
		}

		/// <summary>
		/// Grabs the given <see cref="Grabbable"/> with the given hand.
		/// Returns true if the grab was requested successfully.
		/// </summary>
		public static bool Grab(AHand hand, Grabbable grabbable) {
			if (!CanGrab(hand))
				return false;
			if (grabbable == null) {
				Logger.LogWarning("grabbable is null.", tag: nameof(AutoHandGrab));
				return false;
			}

			EnsureGrabbable(grabbable.gameObject);

			if (!hand.CanGrab(grabbable)) {
				Logger.LogWarning($"cannot grab '{grabbable.name}' with hand '{hand.name}'. " +
				                  "Make sure the hand is free and the grabbable is enabled, grabbable and compatible with the hand.",
				                  tag: nameof(AutoHandGrab));
				return false;
			}

			hand.ForceGrab(grabbable);
			return true;
		}

		/// <summary>
		/// Instantiates a copy of the given prefab and grabs it with the given hand.
		/// Returns the instantiated grabbable, or null on failure.
		/// </summary>
		public static Grabbable GrabCopy(AHand hand, Grabbable prefab) {
			if (!CanGrab(hand))
				return null;
			if (prefab == null) {
				Logger.LogWarning("prefab is null.", tag: nameof(AutoHandGrab));
				return null;
			}

			var copy = UnityEngine.Object.Instantiate(prefab);
			if (!Grab(hand, copy)) {
				UnityEngine.Object.Destroy(copy.gameObject);
				return null;
			}

			return copy;
		}

		/// <summary>
		/// Releases the currently held object (with throw).
		/// </summary>
		public static void Release(AHand hand) {
			if (hand == null)
				return;
			hand.Release();
		}

		/// <summary>
		/// Drops the currently held object without throwing it.
		/// </summary>
		public static void Drop(AHand hand) {
			if (hand == null)
                return;
			hand.ForceReleaseGrab();
		}

		/// <summary>
		/// Ensures the target GameObject is a valid AutoHand grabbable:
		/// non-kinematic, unconstrained Rigidbody, at least one non-trigger collider,
		/// a <see cref="Grabbable"/> component and the "Grabbable" layer.
		/// Returns the <see cref="Grabbable"/> component.
		/// </summary>
		public static Grabbable EnsureGrabbable(GameObject target) {
			if (target == null)
				throw new System.ArgumentNullException(nameof(target));

			// Rigidbody first, so Grabbable.Awake() can find and cache it.
			var body = target.GetComponent<Rigidbody>();
			if (body == null)
				body = target.AddComponent<Rigidbody>();

			var existing = target.GetComponent<Grabbable>();
			var held     = existing != null && existing.IsHeld();

			// AutoHand requires a non-kinematic, unconstrained rigidbody to grab.
			if (!held) {
				body.isKinematic = false;
				body.constraints = RigidbodyConstraints.None;
			}

			// At least one non-trigger collider is required for the palm raycast used by ForceGrab.
			EnsureCollider(target);

			// Add after the body + collider so Grabbable.Awake() picks up the body and grab colliders.
			var grabbable = existing != null ? existing : target.AddComponent<Grabbable>();
			grabbable.body        = body;
			grabbable.enabled     = true;
			grabbable.isGrabbable = true;

			// Make sure the "Grabbable" layer exists before AutoHand writes it to colliders.
			EnsureGrabbableLayer();

			// Re-sync the collider list (required when a collider was just added to an existing grabbable).
			if (!held)
				grabbable.UpdateGrabbableColliderSettings();

			// AutoHand expects grabbables on the "Grabbable" layer so it can highlight and interact with them.
			if (Layers.LayerExists(AHand.grabbableLayerNameDefault) &&
			    target.gameObject.layer != LayerMask.NameToLayer(AHand.grabbableLayerNameDefault))
				target.transform.SetLayer(AHand.grabbableLayerNameDefault);

			return grabbable;
		}

		/// <summary>
		/// Ensures the target has at least one non-trigger collider (adds a BoxCollider if none).
		/// </summary>
		private static void EnsureCollider(GameObject target) {
			var colliders = target.GetComponentsInChildren<Collider>();
			for (var i = 0; i < colliders.Length; i++) {
				if (!colliders[i].isTrigger)
					return;
			}

			// Unity auto-sizes the BoxCollider from the renderer bounds when added at runtime.
			target.AddComponent<BoxCollider>();
		}

		private static void EnsureGrabbableLayer() {
			if (Layers.LayerExists(AHand.grabbableLayerNameDefault))
				return;

#if UNITY_EDITOR
			Layers.CreateLayer(AHand.grabbableLayerNameDefault);
#else
			Logger.LogWarning($"layer '{AHand.grabbableLayerNameDefault}' does not exist. " +
			                  "Create it manually in Project Settings > Tags and Layers.",
			                  tag: nameof(AutoHandGrab));
#endif
		}

		private static bool CanGrab(AHand hand) {
			if (hand == null) {
				Logger.LogWarning("hand is null.", tag: nameof(AutoHandGrab));
				return false;
			}

			if (hand.GetHeld() != null || hand.IsGrabbing()) {
				Logger.LogWarning($"hand '{hand.name}' is already holding or grabbing. Release it first.", tag: nameof(AutoHandGrab));
				return false;
			}

			return true;
		}
	}
}
