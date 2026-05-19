#if  UNITY_EDITOR

using Nox.Avatars.Hand;
using Nox.CCK.Build;
using UnityEditor;
using UnityEngine;
using AHand = Autohand.Hand;

namespace Nox.Avatars.AutoHand {
	public class TestAutoHand : MonoBehaviour, IRemoveOnBuild {
		[ContextMenu("Convert")]
		public void Convert() {
			var hand = GetComponent<IHand>();
			if (hand == null) {
				Debug.LogError("No IHand component found on this GameObject.");
				return;
			}

			Undo.RecordObject(hand.Anchor, "Converted");
			var auto = HandToAutoHand.Convert(hand);
			EditorUtility.SetDirty(hand.Anchor);
			Debug.Log(auto, auto);
		}

		[ContextMenu("Reverse")]
		public void Reverse() {
			var auto = GetComponent<AHand>();
			if (auto == null) {
				Debug.LogError("No AHand component found on this GameObject.");
				return;
			}

			Undo.RecordObject(auto, "Reversed");
			var hand = AutoHandToHand.Convert(auto);
			EditorUtility.SetDirty(auto);
			Debug.Log(hand, hand);
		}
	}
}

#endif