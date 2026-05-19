using System;
using Autohand;
using Nox.Avatars.Hand;
using Nox.CCK.Utils;
using UnityEngine;
using HandType = Nox.Avatars.Hand.HandType;
using AHand = Autohand.Hand;
using AFinger = Autohand.Finger;
using NHand = Nox.CCK.Avatars.Hand.Hand;
using NFinger = Nox.CCK.Avatars.Hand.Finger;

namespace Nox.Avatars.AutoHand {
	public static class AutoHandToHand {
		public static NHand Convert(AHand ah) {
			if (ah == null)
				throw new ArgumentNullException(nameof(ah));

			var hand = ah.GetOrAddComponent<NHand>();
			hand.type   = ah.left ? HandType.Left : HandType.Right;
			hand.anchor = ah.transform;
			hand.palm   = ah.palmTransform;

			hand.fingers = new NFinger[ah.fingers.Length];
			for (var i = 0; i < ah.fingers.Length; i++)
				hand.fingers[i] = Convert(ah.fingers[i]);

			return hand;
		}

		public static NFinger Convert(AFinger af) {
			if (af == null)
				throw new ArgumentNullException(nameof(af));

			var finger = af.knuckleJoint.GetOrAddComponent<NFinger>();
			finger.type = af.fingerType switch {
				FingerEnum.thumb  => FingerType.Thumb,
				FingerEnum.index  => FingerType.Index,
				FingerEnum.middle => FingerType.Middle,
				FingerEnum.ring   => FingerType.Ring,
				FingerEnum.pinky  => FingerType.Pinky,
				_                 => throw new ArgumentOutOfRangeException(nameof(af.fingerType), "Unknown finger type.")
			};

			finger.proximal     = af.knuckleJoint;
			finger.intermediate = af.middleJoint;
			finger.distal       = af.distalJoint;
			finger.tip          = af.tip;
			finger.tipRadius    = af.tipRadius;

			var curls = (FingerCurl[])Enum.GetValues(typeof(FingerCurl));
			finger.poses = new FingerPose[curls.Length];
			for (var i = 0; i < curls.Length; i++) {
				var curl = curls[i];
				var pose = new FingerPose { curl = curl };
				if (curl != FingerCurl.TPose) {
					var ai = curl.ToAuto();
					if (af.poseData != null && (int)ai < af.poseData.Length)
						pose.values = af.poseData[(int)ai].localRotations;
				}
				finger.poses[i] = pose;
			}

			return finger;
		}
	}
}
