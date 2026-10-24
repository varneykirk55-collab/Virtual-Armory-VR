# 🛡️ Virtual Armory VR | Meta Quest 2

**Virtual Armory VR** is an immersive virtual reality training experience developed for **Meta Quest 2** using **Unity** and the **XR Interaction Toolkit**. This project is designed as a standalone VR experience that introduces users to the basics of armory combat using realistic motion-controlled weapons.

---

## 🎥 Demo Video

[![Watch Demo](https://img.youtube.com/vi/94Iz-lMDz9Q/0.jpg)](https://youtu.be/94Iz-lMDz9Q)

---

## 🕹️ Features

- 🧍‍♂️ **3 Training Dummies** triggered by pressure plates
- 🔫 **Interactive Weapons**:  
  - **Pistol** – Fire via hand gesture  
  - **Sword** – Swing for melee attacks  
  - **Shield** – Block and defend
- 🧠 **Physics-Based Interactions** for realistic training feedback
- 👐 **Hand-tracked Controls** using Meta Quest 2 controllers
- 🔊 **Responsive Sound Effects** for shooting, striking, and dummy reactions
- 🎯 **Beginner-Friendly Layout** – drop users straight into the action

---

## 🧭 Screenshots



### 🖼️ Arena Overview

![Arena Overview](images/arena-overview1.png)
![Arena Overview](images/arena-overview2.png)

---

## 🚀 Getting Started

### 🧰 Requirements

- Unity 2022.3+ (with Android Build Support)
- XR Plugin Management
- XR Interaction Toolkit
- Meta Quest 2 + Link Cable or Wireless ADB setup

### 📦 Build Instructions

1. Clone or download this repository.
2. Open it in Unity Hub.
3. Go to **Project Settings > XR Plugin Management** and enable **Oculus** under Android.
4. Connect your Quest 2 and build to Android platform.
5. Deploy via Unity Build or SideQuest.

---

## ⚔️ Samurai Duel (one-on-one combat prototype)

A scripted samurai opponent built on top of the existing XR rig, swords and shields. Architecture notes and the file plan are in [`Docs/SAMURAI_DUEL_PLAN.md`](Docs/SAMURAI_DUEL_PLAN.md). The code is in `Assets/SamuraiDuel/`.

### One-time setup in the editor
1. Open the project in **Unity 2022.3.44f1** (with Android Build Support, plus OpenJDK and the Android SDK & NDK tools).
2. Open `Assets/Scenes/SampleScene.unity`.
3. Run **Tools ▸ Samurai Duel ▸ Build Duel In Open Scene**. This:
   - builds the samurai, HUD, red start plate and arena ring 4 m in front of the XR Origin,
   - adds `SwordHitDetector` + `Blocker` to the swords and a `Blocker` to the shields,
   - adds `PlayerCombatant` (head/torso hurtbox, HP, damage flash) to the XR Origin,
   - turns off any training dummy standing inside the ring.

   Move the `SamuraiDuel` object if you want the ring somewhere else.
4. Select each sword and check the cyan **BladeBase → BladeTip** gizmo runs along the blade. If it doesn't, drag the two child markers.
5. Run **Tools ▸ Samurai Duel ▸ Apply Quest 2 Build Settings** (Android target, IL2CPP, ARM64, min API 29, 72 Hz physics).
6. Save the scene.

### How to play
- Grab a sword (grip). A shield in the other hand is optional.
- Step on the **red plate** or press **A/X** to start. After a 3-second countdown: *Hajime!*
- **Attacking**: swing for real. Damage scales with blade speed, and slow pokes don't count. Head hits do ×1.5. Hitting the samurai during its wind-up interrupts the attack. It is armoured only during the cut itself.
- **Defending**: when its blade glows red, the cut is coming from where the sword is raised. Block it with the shield or your sword. Swing *into* the cut to **parry**, which staggers it for a free opening. You can also step back about half a metre.
- Combo counter, score and a saved best score are shown on the scoreboard. Hold **A/X** for 1 s to restart mid-fight. After victory or defeat, press **A/X** or step on the plate.
- In the editor, the XR Device Simulator works, and **R** restarts.

### Build & sideload to Quest 2
1. Turn on developer mode for the headset in the Meta Horizon phone app (Devices ▸ Headset settings ▸ Developer mode). Connect it with USB-C and accept **Allow USB debugging** inside the headset.
2. **File ▸ Build Settings** ▸ Platform **Android** (Switch Platform if needed). Make sure `Scenes/SampleScene` is ticked. Set **Run Device** to your Quest.
3. **Project Settings ▸ XR Plug-in Management ▸ Android tab**: tick **Oculus**.
4. Optional: in **Player ▸ Android ▸ Other Settings**, give the app a unique **Package Name** (e.g. `com.yourname.samuraiduel`) so it doesn't collide with other template builds.
5. **Build And Run** installs and launches it. Or **Build** an `.apk` and sideload it with `adb install -r SamuraiDuel.apk` or SideQuest.
6. On the headset: **Library ▸ Unknown Sources ▸ project showcase VR**.

---

## 📘 Documentation

- 📄 [Game Design Document (GDD)](./Armory%20Practice%20GDD.docx)

---

## 📌 Project Goals

This project was built as a solo learning prototype for VR development. The main goal was to understand interaction systems, weapon mechanics, and basic VR UX through practical experimentation.

---

## 🔧 Tech Stack

| Tool            | Purpose                        |
|-----------------|--------------------------------|
| Unity (2022.3+) | Game development platform      |
| C#              | Core gameplay scripting         |
| XR Toolkit      | VR Interaction & Input System   |
| Meta Quest 2    | Standalone VR Hardware Target  |

---

## 👨‍💻 Author

**ANS1087**

💼 [LinkedIn Profile](https://www.linkedin.com/in/shreykhetan/)  

📬 [Email](mailto:Shreykhetan@gmail.com)

---

> *This is a practice and demo project built as part of my journey into XR and VR development. Feedback is welcome!*
