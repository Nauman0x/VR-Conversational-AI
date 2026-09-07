# VR-ASSM: Virtual Reality AI-Powered Social Skills Mentor

---

**Project Name:** VR-ASSM — Virtual Reality AI-Powered Social Skills Mentor

**Team Members & Registration Numbers:**

| Name | Registration Number | Role |
|---|---|---|
| Nauman | [REG-001] | AI Engineer |
| Muzammil | [REG-002] | Backend Developer |
| Esha | [REG-003] | Avatar & Animation Engineer |
| Saleha | [REG-004] | XR Developer |
| Aena | [REG-005] | UI/UX Designer & Developer |
| Meerub | [REG-006] | QA & Content Specialist |

**Course Name:** Extended Reality (XR) Design and Development

**Instructor Name:** [Instructor Name]

**Submission Date:** May 2, 2026

---

---

# 1. Introduction (Project Overview)

## 1.1 Background

Mental health challenges, social anxiety, and interpersonal communication difficulties are among the most prevalent issues affecting university students and young adults globally. According to the World Health Organization, approximately 1 in 4 people will be affected by a mental or neurological disorder at some point in their lives, and social skills deficits are a core contributor to anxiety disorders, depression, and poor quality of life. Traditional approaches to social skills training — therapy sessions, group workshops, or role-play exercises with human coaches — are often expensive, time-limited, and inaccessible to a large portion of the population.

Conversational practice is a foundational element of cognitive behavioural therapy (CBT) and social skills training. However, many individuals experiencing social anxiety are unwilling or unable to engage in face-to-face role-play exercises due to the very anxiety they are trying to overcome. This creates a paradox: the most effective treatment modality is also the most intimidating entry point for the target population.

Virtual Reality (VR) offers a powerful solution to this paradox. By immersing users in a safe, controlled, and repeatable virtual environment, VR can provide the psychological presence of a real social interaction without the social stakes that make real-world practice so daunting. Research consistently demonstrates that experiences in VR feel real enough to trigger the emotional and cognitive responses that drive learning and behaviour change — a quality known as "presence" — while simultaneously remaining private, judgment-free, and fully under the user's control.

This project therefore sits at the intersection of VR technology, conversational AI, and applied psychology, applying immersive Extended Reality techniques to a problem of genuine societal importance.

---

## 1.2 Problem Statement

Current systems for social skills development and mental wellness conversation practice have several critical gaps:

**What is missing in current systems:**
- Traditional therapy and coaching is expensive, with average session costs of $100–$300 USD per hour, putting it out of reach for most students and young adults.
- Mobile-based mental health apps (such as Woebot or Wysa) deliver text-only conversation, which lacks the embodied presence and emotional realism necessary for genuine skill transfer.
- Existing VR therapy tools are primarily research prototypes — they are not deployable consumer products and do not feature real-time natural language conversation driven by modern large language models (LLMs).
- No existing accessible solution combines persistent memory of prior sessions, personalised conversation, and lifelike avatar interaction in a deployable VR application.

**Difficulty users face:**
- Individuals with social anxiety are unwilling to begin face-to-face practice because the perceived social risk is too high, even in therapeutic settings.
- Users who do engage with text-based AI chatbots report feelings of disconnection and inauthenticity, reducing therapeutic efficacy.
- Without continuity between sessions (i.e., the AI remembering past conversations), users must repeatedly reintroduce context, which breaks immersion and erodes the sense of a genuine therapeutic relationship.

VR-ASSM directly addresses each of these gaps by providing a low-cost, immersive, personalized, and memory-enabled VR mentor powered by modern conversational AI.

---

## 1.3 Project Objectives

1. Develop a functional VR application for Meta Quest (OpenXR) in which a user can hold a natural, real-time voice conversation with a lifelike AI avatar trained on psychology and social skills literature.
2. Implement persistent conversation memory by storing session summaries in a PostgreSQL database and injecting them as context at the beginning of each new session, enabling personalised and progressive mentoring.
3. Achieve realistic avatar expressiveness through Oculus LipSync-driven blend shape animation and Unity Animator state machines, creating a sense of genuine presence.
4. Design an accessible dual-mode interface (VR headset and desktop keyboard/mouse) to allow the application to run and be tested without mandatory VR hardware.
5. Build a complete user authentication system backed by PostgreSQL to support multi-user deployments where each user's history is securely segregated.
6. Enable multi-environment interaction (indoor and outdoor nature settings) to provide variety and support context-appropriate conversation scenarios.

---

---

# 2. Literature Review / Existing Solutions

## 2.1 Similar Applications

### 2.1.1 Oxford VR — "gameChange"
gameChange is a clinically validated VR therapy product developed by Oxford VR, a spin-out from the University of Oxford. It targets severe agoraphobia by placing patients in everyday virtual environments (a café, a bus, a waiting room) and coaching them through cognitive behavioural exercises using an automated virtual coach. The system has been trialled in NHS mental health services in the UK and demonstrated statistically significant improvements in agoraphobia severity and quality of life scores versus standard care (Freeman et al., 2022). gameChange uses scripted, branching dialogue trees to drive the virtual coach's responses rather than live generative AI, which limits the naturalness and adaptability of conversation.

### 2.1.2 Limbix VR
Limbix is a digital therapeutics company that offers VR-based experiences targeting adolescent anxiety and depression. Their platform delivers 360-degree immersive video experiences paired with guided mindfulness and CBT exercises. Unlike game-engine-based systems, Limbix uses video content, which offers high visual realism but zero interactivity — users are passive viewers, not active conversational participants. There is no AI avatar, no microphone input, and no personalisation based on individual history.

### 2.1.3 Convai NPC Conversations
Convai (convai.com) offers a platform for embedding generative AI conversations into game NPCs, including a Unity SDK. Convai agents can respond dynamically to spoken input and maintain short-term contextual awareness. However, Convai is a general-purpose NPC toolkit — it does not specialise in social skills mentoring, does not integrate psychology knowledge bases, and does not include persistent cross-session memory backed by a relational database.

---

## 2.2 Gap Analysis

| Feature | gameChange | Limbix | Convai SDK | VR-ASSM (This Project) |
|---|---|---|---|---|
| Real-time generative AI conversation | ✗ | ✗ | ✓ | ✓ |
| Persistent cross-session memory | ✗ | ✗ | ✗ | ✓ |
| Psychology-trained knowledge base | ✓ | ✓ | ✗ | ✓ |
| Live voice I/O with lip sync | ✓ | ✗ | ✓ | ✓ |
| User authentication & profiles | ✗ | ✗ | ✗ | ✓ |
| Open / deployable on Meta Quest | ✗ | ✗ | ✓ | ✓ |
| Session summary & personalisation | ✗ | ✗ | ✗ | ✓ |

**What can be improved / What VR-ASSM adds:**

- **Generative AI conversation:** Unlike gameChange and Limbix which rely on scripted content, VR-ASSM uses ElevenLabs Conversational AI backed by an LLM to generate novel, contextually appropriate responses in real time. This enables conversations that feel genuinely responsive rather than following a predetermined script.
- **Persistent memory and personalisation:** No existing consumer-facing VR social skills tool maintains a history of past sessions and uses it to personalise future interactions. VR-ASSM's PostgreSQL-backed conversation history and OpenAI-powered summarisation pipeline directly address this gap.
- **Psychology knowledge base:** The ElevenLabs agent is specifically trained on psychology and social skills literature, ensuring responses are grounded in evidence-based therapeutic principles rather than general conversational patterns.

---

---

# 3. Target Audience

VR-ASSM is designed for the following primary and secondary user groups:

**Primary Users — University Students with Social Anxiety:**
Young adults (ages 18–26) enrolled in higher education who experience mild-to-moderate social anxiety, communication apprehension, or interpersonal skills deficits. This group is digitally native, likely to own or have access to VR hardware or a capable PC, and is typically underserved by traditional mental health services due to cost and stigma barriers.

**Secondary Users — Professionals Seeking Communication Coaching:**
Working adults (ages 25–45) who wish to improve professional communication skills — public speaking confidence, interview preparation, or conflict resolution — in a private, self-directed environment. These users are motivated by career development rather than clinical need.

**Tertiary Users — Therapists and Educators:**
Mental health practitioners and educators who may deploy VR-ASSM as a supplementary homework tool between therapy sessions, or as a classroom demonstration of AI-assisted social skills training.

**User Characteristics:**
- Basic VR literacy or willingness to learn (for headset deployment)
- English language proficiency (primary language of the AI agent)
- Access to a Meta Quest headset or a Windows PC with microphone
- Comfort with voice-based interaction (no typing required)

---

---

# 4. Use Case & Task Analysis (Scenario)

## 4.1 Main User Scenario

**Scenario: "First Session — Ahmed's Introduction"**

Ahmed is a second-year university student who struggles with social anxiety and finds it difficult to initiate conversations with peers. He has heard about VR-ASSM from a university counselling service. He puts on a Meta Quest 3 headset and launches the application. He is greeted by a sign-in screen; since he is new, he creates an account. After signing in, he finds himself standing in a warmly lit indoor room facing Mike, a lifelike male avatar. Ahmed presses the call button (A button on the controller) and introduces himself hesitantly. Mike, the AI mentor, responds warmly in a natural voice, asking Ahmed what he'd like to work on today. They discuss Ahmed's anxiety about group presentations. Partway through the session, Ahmed presses the environment toggle and the scene transitions to a peaceful outdoor nature setting, which Mike acknowledges conversationally. At the end of the session, Ahmed presses the call button again to end the call. The system saves a summary of the conversation. The next time Ahmed logs in, Mike will already "remember" that they discussed presentation anxiety, allowing the session to build on the previous one.

---

## 4.2 Task Flow

**Step 1 — User Launches Application**
- User puts on Meta Quest headset (or opens the Unity Editor in desktop mode).
- Application loads `SampleScene.unity`.
- User is presented with a sign-in / sign-up panel rendered on a world-space Canvas.

**Step 2 — User Authenticates**
- New users: enter name, age, and a brief self-introduction → account created in PostgreSQL `users` table.
- Returning users: enter their User ID → authenticated against the database.
- On success, the auth panel dismisses and the avatar becomes visible.

**Step 3 — User Selects Environment**
- Default environment is the indoor scene (SampleScene).
- User can press the Environment Toggle button (on the in-world Canvas UI) at any time to teleport the XR Origin and avatar to the Nature outdoor environment and back.
- No scene reload occurs; the call continues uninterrupted across environments.

**Step 4 — User Initiates Conversation**
- User presses the A/X button (VR) or C key (Desktop) to start the call.
- `AvatarAIController` opens a WebSocket connection to ElevenLabs Conversational AI.
- Past session history from PostgreSQL is injected as context to personalise the agent.
- The avatar transitions from idle to a listening/ready state.

**Step 5 — User Interacts with Objects / Avatar**
- User speaks naturally into the microphone.
- ElevenLabs processes voice → generates AI response → streams audio back.
- Oculus LipSync drives the avatar's lip blend shapes in real time synchronised with the audio.
- The avatar plays talking/idle animations through the Unity Animator.
- User can barge in (speak while the avatar is talking) to interrupt at any time.
- User can switch between Mike and Anna avatars mid-session via the AvatarSwitcher.

**Step 6 — User Receives Feedback**
- On-screen status UI shows the current conversation state (Listening / Processing / Responding).
- Transcribed text is displayed for user verification.
- The avatar's facial animation provides real-time non-verbal feedback.

**Step 7 — User Exits System**
- User presses the call button again to end the conversation.
- `ConversationHistoryExporter` fetches the session transcript from ElevenLabs REST API.
- OpenAI GPT-4o-mini summarises the transcript.
- Summary and transcript are stored in PostgreSQL `conversation_history` table.
- User can start a new session, switch avatars, change environment, or close the application.

---

---

# 5. XR Technology Selection

## 5.1 Selected Technology: Virtual Reality (VR)

**Why VR, not AR or MR:**

Virtual Reality was selected over Augmented Reality (AR) and Mixed Reality (MR) for the following reasons:

- **Immersive presence:** VR replaces the real world entirely with a controlled virtual environment, maximising the sense of "being there" with the avatar. For social anxiety training, full immersion is critical — the user must feel the presence of the virtual interlocutor, not just see an overlay on their real-world surroundings.
- **Distraction elimination:** AR/MR experiences are inherently susceptible to real-world distractions, which would undermine the focused therapeutic context required for social skills mentoring.
- **Avatar quality:** High-fidelity humanoid avatars with real-time lip sync and animation require a fully rendered 3D environment. VR provides the rendering pipeline and presence depth that makes the avatar feel like a genuine social partner.
- **Research validation:** The majority of evidence-based XR therapy research has been conducted in VR (particularly using headsets), providing a stronger evidentiary basis for VR as the modality of choice for anxiety and social skills interventions.

---

## 5.2 Hardware Platform

| Platform | Role |
|---|---|
| **Meta Quest ** | Primary VR deployment target (Android/OpenXR build) |
| **Windows PC (SteamVR / Oculus PC App)** | Secondary deployment for desktop-tethered VR |
| **Windows PC (No Headset)** | Desktop testing mode with arrow key movement and keyboard call control |
| **Microphone (built-in to headset or PC)** | Voice input for conversation |

**Minimum PC specifications (desktop/tethered mode):**
- Windows 10 64-bit or newer
- 8 GB RAM (16 GB recommended)
- DirectX 11 compatible GPU
- Stable internet connection

---

## 5.3 Software & Tools

| Category | Tool / Technology | Version |
|---|---|---|
| Game Engine | Unity | 2022.3.48f1 (LTS) |
| VR Framework | OpenXR | 1.12.1 |
| XR Interaction Toolkit | Unity XRIT | 2.6.5 |
| XR Management | Unity XR Management | — |
| Conversational AI | ElevenLabs Conversational AI (WebSocket) | — |
| LLM Summarisation | OpenAI GPT-4o-mini | — |
| Database | PostgreSQL + Npgsql.dll | Npgsql 7.x |
| Lip Sync | Oculus LipSync SDK | — |
| Avatar Pipeline | Convai SDK + CC3 Unity Tools | — |
| Input Handling | Unity Input System | 1.11.0 |
| JSON Serialization | Newtonsoft.Json (Unity) | 3.2.1 |
| Text Rendering | TextMesh Pro | 3.0.9 |
| Animation Rigging | Unity Animation Rigging | 1.2.1 |
| Build Tools | Android Build Support + OpenJDK + NDK | (bundled with Unity Hub) |
| Version Control | Git | — |

---

---

# 6. System Requirements

## 6.1 Functional Requirements

| ID | Requirement |
|---|---|
| FR-001 | The system shall capture real-time microphone audio input from the user and stream it to ElevenLabs Conversational AI over a secure WebSocket connection. |
| FR-002 | The system shall authenticate users against a PostgreSQL database, supporting both new user registration (sign-up) and returning user sign-in, with session persistence across scenes. |
| FR-003 | The system shall retrieve the authenticated user's past conversation summaries from PostgreSQL and inject them as contextual prompt information at the start of each new AI conversation session, enabling personalised responses. |
| FR-004 | The system shall animate the avatar's lip movements in real time synchronised with the AI-generated audio response, using Oculus LipSync viseme processing and Unity blend shape animation. |
| FR-005 | The system shall summarise the conversation transcript at the end of each session using OpenAI GPT-4o-mini and persist the summary and raw transcript to the PostgreSQL `conversation_history` table for use in future sessions. |
| FR-006 | The system shall support barge-in / interruption, allowing the user to speak while the avatar is responding, causing the avatar to stop speaking and listen to the new input. |
| FR-007 | The system shall allow the user to switch between two selectable avatars (Mike and Anna) during a session without restarting the application or the conversation. |
| FR-008 | The system shall provide an environment toggle that teleports both the user's XR Origin and the avatar between the indoor SampleScene environment and the outdoor Nature environment without reloading the scene or interrupting an active conversation. |
| FR-009 | The system shall support dual-mode input: VR controller (A/X primary button) for Meta Quest headset operation, and keyboard (C key + arrow keys + mouse) for desktop/editor testing without a headset. |
| FR-010 | The system shall display real-time status indicators in the UI (Idle / Listening / Processing / Responding) so the user always knows the current state of the conversation pipeline. |

---

## 6.2 Non-Functional Requirements

| ID | Requirement |
|---|---|
| NFR-001 | **Performance:** The application shall maintain a minimum of 30 frames per second during active conversation on the target Meta Quest hardware. Total conversation turn-around time (user speech end → avatar begins responding) shall not exceed 12 seconds under normal network conditions. |
| NFR-002 | **Reliability:** The system shall handle API failures (ElevenLabs, OpenAI, PostgreSQL) gracefully without crashing the application, providing user-facing error messages and returning the system to a recoverable state. Network interruptions shall not result in data loss for conversation history; failed export attempts shall be logged for manual recovery. |
| NFR-003 | **Security:** API keys (ElevenLabs, OpenAI) and database credentials (PostgreSQL host, user, password) shall never be hardcoded in source files committed to version control. In production, these values shall be managed via Unity Inspector fields on scene GameObjects and excluded from the repository via `.gitignore`. All communication with external services shall occur exclusively over HTTPS or WSS (secure WebSocket). |
| NFR-004 | **Usability:** A first-time user with no prior VR experience shall be able to successfully sign up, initiate a conversation with the avatar, and end the session without consulting documentation, relying only on in-world UI labels and standard VR controller conventions. |
| NFR-005 | **Maintainability:** Each AI service integration (ElevenLabs, OpenAI, database) shall be encapsulated in a dedicated MonoBehaviour component (`AvatarAIController`, `ConversationHistoryExporter`, `UIManager`), allowing any single service to be updated, replaced, or disabled without modifying unrelated components. |

---

---

# 7. Storyboarding

## 7.1 Storyboard Description

**Where does the experience start?**
The experience begins inside a warmly lit virtual indoor room — the `SampleScene` environment. The user, represented by an invisible first-person XR rig, is standing approximately 1.5 metres in front of Mike, a high-fidelity male avatar in a relaxed idle animation. Directly in front of the user, slightly below eye level, a world-space Canvas panel displays the sign-in / sign-up UI. The room feels calming and neutral, deliberately designed to reduce rather than heighten anxiety.

**What does the user see first?**
The user sees Mike standing quietly, and immediately in front of them, the authentication panel: two input fields (User ID), a Sign In button, and a Register option. The avatar is visible behind the panel but not yet interactive. A subtle ambient audio track plays softly in the background.

**What is the main interaction?**
The primary interaction is voice conversation. Once authenticated and the call is started, the user speaks freely with the avatar using natural language. The avatar listens, responds with a voice and animated mouth, and maintains eye contact with the user through a look-at constraint. The user can speak, pause, barge in to interrupt, switch avatars, or toggle the environment — all through simple button presses with no menu navigation required.

**How does the environment change?**
At any point during a session, the user may press the Environment Toggle button on the in-world UI. This triggers `EnvironmentTeleporter`, which moves the XR Origin (the user's position) and the avatar to the Nature environment coordinates within the same scene — transitioning from the indoor room to an open outdoor space with trees, sky, and ambient natural sounds. The conversation continues without interruption. The user may toggle back to the indoor environment at any time.

**How does the experience end?**
The user presses the call button (A/X on VR, C key on desktop) a second time to end the active call. The avatar returns to its idle animation. Behind the scenes, `ConversationHistoryExporter` fetches the transcript, generates a summary, and stores it in PostgreSQL. The user sees a "Call Ended" status message. They may start a new call, which will benefit from the memory of the session just completed, or close the application.

---

## 7.2 Storyboard Frames

> **Note:** The following six storyboard frames must be drawn by hand using the Vincent sketch template. Each frame description below specifies the scene composition, user viewpoint, visible UI elements, avatar state, and permitted interactions. Recreate each frame on the Vincent template paper and attach scanned or photographed images in this section.

---

**Frame 1 — Application Launch / Authentication Screen**
- *Viewpoint:* First-person VR perspective. User is standing, looking slightly downward toward a floating Canvas panel.
- *Scene elements:* Mike avatar standing behind the panel in idle pose. Dimly lit indoor room visible. Panel shows two input fields labelled "User ID" and "Password / Name", a "Sign In" button, and a "Register" link.
- *Avatar state:* Idle breathing animation. Not yet interactive.
- *Interactions:* User uses VR controller ray or desktop mouse to click UI fields and buttons. Text entry via virtual keyboard (VR) or physical keyboard (desktop).

---

**Frame 2 — Post-Authentication / Pre-Call State**
- *Viewpoint:* First-person VR. User is standing; the auth panel has disappeared. Mike is now fully visible and slightly closer.
- *Scene elements:* In-world call button ("Start Call") and environment toggle visible at the bottom of the user's view. Avatar in idle. Status indicator shows "Ready".
- *Avatar state:* Idle loop — gentle breathing, subtle eye blink, looking at user.
- *Interactions:* User can press the Start Call button (on Canvas), press the A/X controller button, or press C key to begin the call. Environment toggle is also available.

---

**Frame 3 — Active Conversation (User Speaking)**
- *Viewpoint:* First-person VR. User is looking directly at Mike's face at natural eye level.
- *Scene elements:* Microphone icon pulses in the UI corner. Status indicator shows "Listening". Avatar has a slightly attentive expression.
- *Avatar state:* Idle pose with subtle forward-lean attention animation. Mouth closed (not yet speaking).
- *Interactions:* User speaks into microphone. Barge-in is active — user can speak at any time. Avatar Switcher button visible in peripheral UI.

---

**Frame 4 — Active Conversation (Avatar Responding)**
- *Viewpoint:* First-person VR. Close-up view of Mike's face. Lip sync animation is visible — mouth shapes match the audio phonemes.
- *Scene elements:* Status indicator shows "Responding". Audio waveform visual indicator active. Transcript text appears below the avatar name label.
- *Avatar state:* Talking animation — lip blend shapes driven by Oculus LipSync in real time. Subtle hand gesture animation if configured.
- *Interactions:* User can interrupt (barge-in) by speaking. All other buttons remain accessible.

---

**Frame 5 — Environment Toggle (Nature Scene)**
- *Viewpoint:* First-person VR. User has pressed Environment Toggle. They are now outdoors.
- *Scene elements:* Trees, grass, open sky. Mike avatar has teleported to the same relative position in front of the user. Conversation UI persists. Status shows "Responding" or "Listening" (call is uninterrupted).
- *Avatar state:* Same as prior state — conversation continues without reset.
- *Interactions:* All prior interactions available. Toggle button now shows "Return to Room".

---

**Frame 6 — Session End / History Saved**
- *Viewpoint:* First-person VR. User has pressed the call button to end the session.
- *Scene elements:* Status indicator shows "Call Ended — Summary Saved". Mike has returned to idle. A small notification card or toast message in the UI confirms "Your session has been saved."
- *Avatar state:* Idle. Relaxed posture. Looks at user.
- *Interactions:* User can start a new call (which will now have memory of this session), switch avatar, toggle environment, or close the application.

---

## 7.3 Storyboard Analysis Question

**How does your storyboard support user immersion, usability, and learning/experience goals?**

**Scene 1 (Authentication):** The auth screen floats naturally in the user's field of view in world space — not as a flat 2D menu but as a panel that exists within the virtual room. Mike is visible in the background, establishing the avatar's presence from the very first moment. This serves immersion by making the avatar a known, anticipated presence before any interaction, reducing the "cold start" anxiety that a sudden appearance could cause. The authentication step also serves a usability goal: by requiring sign-in, the system communicates to the user that it will remember them, establishing the expectation of continuity.

**Scene 2 (Pre-Call State):** The simplicity of the pre-call state — avatar visible, one clear call button, minimal UI — directly supports usability. There are no menus to navigate, no settings to configure. The primary action is immediately apparent. This scene also supports the learning goal: the user is not thrown into immediate interaction; they have a moment to orient themselves, observe the avatar, and feel ready. This micro-transition from passive observation to active engagement mirrors therapeutic exposure principles.

**Scenes 3 & 4 (Conversation):** The close-up perspective on the avatar's face during conversation maximises the lip-sync impact and directs the user's social attention to the avatar's facial cues — exactly as they would attend to a real human face in conversation. This is the core immersion mechanism: the avatar's responsive face, animated in real time to the AI voice, creates the feeling of genuine presence. Real-time status indicators (Listening / Responding) serve usability by keeping the user oriented in the conversational flow, preventing the confusion of "is it my turn to speak?"

**Scene 5 (Environment Toggle):** The nature environment change serves both variety and a therapeutic metaphor — moving from an enclosed indoor space to an open outdoor space can symbolise openness, calm, and the transfer of learned skills to real-world settings. The fact that conversation continues uninterrupted demonstrates to the user that they are in control of their environment, which is empowering. This scene supports the experience goal of environmental agency.

**Scene 6 (Session End):** The explicit confirmation that the session has been saved is a critical usability and learning moment. It communicates to the user that their work matters and will be remembered, encouraging return usage. The persistence of conversation memory is the key differentiator of VR-ASSM, and Scene 6 is where that value proposition is made tangible and visible to the user.

---

---

# 8. Development

## 8.1 Team Contributions and Responsibilities

| Team Member | Contributions |
|---|---|
| **Nauman** | Designed and implemented the PostgreSQL database schema (`users` and `conversation_history` tables). Built the `UIManager.cs` sign-in/sign-up system with Npgsql reflection-based database connectivity. Integrated the `ConversationHistoryExporter.cs` pipeline: ElevenLabs transcript fetch → OpenAI GPT-4o-mini summarisation → PostgreSQL persistence. Configured the prompt context injection in `AvatarAIController.cs` to retrieve user history at session start. |
| **Muzammil** | Implemented the ElevenLabs Conversational AI WebSocket integration in `AvatarAIController.cs`, including signed URL negotiation, PCM audio streaming, barge-in logic, and message protocol handling. Engineered the agent's system prompt using psychology book content and conversation history context templates for personalised responses. |
| **Esha** | Set up the Mike Carter and Anna avatars using Convai SDK and Character Creator (CC3 Unity Tools). Configured the Oculus LipSync SDK viseme pipeline and mapped phoneme visemes to avatar blend shape indices. Built and tuned the Unity Animator state machine for idle, talking, and transition animations. Implemented `OculusLipSyncBlendShape.cs` for real-time blend shape driving. |
| **Saleha** | Set up the Unity XR Interaction Toolkit (XRIT) and OpenXR configuration for Meta Quest. Implemented `VRLocomotionController.cs` (joystick movement, snap/smooth turn) and `VRConversationControls.cs` (primary button mapping). Managed the Android build pipeline, Meta Quest Developer Mode setup, and APK deployment testing. Configured `DesktopArrowKeyMovement.cs` for editor testing. |
| **Aena** | Designed and implemented all UI panels: authentication screens, call button, status indicators, avatar switcher button, and environment toggle button using Unity UGUI and TextMesh Pro. Managed world-space Canvas layout, scene hierarchy organisation, and visual design consistency. Built `NatureSceneUI.cs` for environment-specific UI adjustments. |
| **Meerub** | Researched and curated the psychology and social skills knowledge base used to train the ElevenLabs agent. Wrote the agent's system prompt guidelines, personality specification, and safety filtering rules. Developed the QA test plan and scripted test scenarios. Conducted multi-session testing, filed bug reports, and validated conversation quality and memory persistence across sessions. |

---

## 8.2 Application Deployment Status

The application is in a running and deployable state:

- **Desktop/Editor Mode:** Fully functional in Unity 2022.3.48f1 on Windows. Launch SampleScene, press Play, and interact using arrow keys and the C key.
- **Meta Quest (VR Build):** Built and deployed as an Android APK on Meta Quest 2/3 via USB with Android Build Support and OpenXR. The primary button (A/X) controls call start/end; joystick controls locomotion.

---

## 8.3 Application Screenshots

> **Note:** Attach at least 5 screenshots of the running application here. Recommended screenshots:
>
> 1. Authentication / sign-in panel in the Unity Editor Play mode
> 2. Avatar (Mike) in idle state in the indoor SampleScene environment
> 3. Active conversation — avatar mid-speech with lip sync animation visible
> 4. Nature environment — avatar and user teleported outdoors
> 5. Meta Quest headset view — in-VR first-person perspective during a call (captured via Meta Quest casting or developer screenshot tool)

---

---

# 9. Limitations & Risks

## 9.1 Technical Limitations

- **WebSocket dependency on ElevenLabs:** The entire primary conversation pipeline (STT, LLM, TTS) runs through ElevenLabs Conversational AI over a WebSocket. If ElevenLabs service experiences downtime or API changes, the application becomes non-functional for conversation. There is no offline fallback.
- **Reflection-based Npgsql loading:** The PostgreSQL connection is established via .NET reflection to load the Npgsql assembly at runtime (to avoid Unity build-time issues). This approach is brittle — it will silently fail if the Npgsql.dll version is incompatible or missing, with no user-facing error beyond a debug log.
- **No real-time streaming TTS lip sync:** Lip sync is driven by the audio clip after it has been received from ElevenLabs, not by a real-time phoneme stream. If the audio is delayed, the lip animation is delayed identically, which can feel unnatural in high-latency conditions.
- **Single scene database context:** The PostgreSQL context injection queries a fixed number of past sessions (`promptContextHistoryLimit`). Very long-term users may find that their earliest sessions are no longer reflected in the agent's responses, limiting the depth of the memory system.

## 9.2 Hardware Constraints

- **Compute budget on Quest:** The Meta Quest 2/3 has limited GPU and CPU resources compared to PC VR. High-fidelity avatar rendering with real-time lip sync, Oculus LipSync processing, and Unity XRIT locomotion simultaneously can push the Quest's thermal and compute limits, potentially causing frame rate drops below the comfortable 72Hz threshold.
- **Microphone quality varies:** The built-in microphones on Meta Quest headsets are optimised for positional audio, not necessarily for clean speech capture in noisy environments. Background noise can degrade ElevenLabs STT accuracy.
- **Quest connectivity:** Meta Quest requires a stable Wi-Fi connection for all API calls (ElevenLabs, OpenAI, PostgreSQL). Poor Wi-Fi coverage in a play space will result in connection timeouts and broken conversations.

## 9.3 Skill Limitations

- **C# async/await in Unity:** Unity's threading model is not standard .NET; `async`/`await` operations must be carefully marshalled back to the main thread to touch GameObjects. The team has implemented this correctly using `SynchronizationContext`, but subtle race conditions in the WebSocket pipeline may surface under edge-case timing scenarios that were not covered in testing.
- **Avatar blend shape mapping:** Correctly mapping Oculus LipSync viseme indices to the specific blend shapes of the CC3 Mike and Anna avatars required manual verification of dozens of blend shape names. Any update to the avatar mesh (e.g., from Convai SDK updates) could silently break the lip sync mapping.

## 9.4 Time Limitations

- **Single-language support:** Due to project timeline constraints, the ElevenLabs agent and the entire UI are English-only. Multilingual support, which would significantly expand the target audience, was deferred.
- **Limited session QA coverage:** Multi-session memory persistence was tested across a limited number of session cycles (approximately 5–10 sessions per test user). Edge cases such as very long transcripts exceeding OpenAI token limits, or PostgreSQL connection pool exhaustion under concurrent users, were identified but not fully resolved within the project timeline.
- **Storyboard completeness:** The storyboard was designed and described but requires physical hand-drawn frames on the Vincent sketch template, which must be completed, scanned, and inserted into this document before final submission.

---

---

# 10. Conclusion

VR-ASSM (Virtual Reality AI-Powered Social Skills Mentor) demonstrates that immersive VR technology and modern generative AI can be meaningfully combined to address a real, underserved human need: accessible, private, and personalised social skills mentoring for individuals experiencing social anxiety or communication challenges.

The project successfully delivers a deployed, functional VR application on Meta Quest that enables users to hold natural, real-time voice conversations with a lifelike avatar, powered by ElevenLabs Conversational AI and enriched by a persistent PostgreSQL-backed memory system. Users can practice conversation in two distinct environments, switch between two avatars, and return to subsequent sessions with the AI mentor already knowing their history — a capability that no comparable existing consumer VR product offers.

The development process demonstrated effective multi-disciplinary teamwork, integrating expertise in AI engineering, backend development, avatar animation, XR interaction design, UI/UX, and domain-specific content curation. Each team member's contribution was essential to the coherence of the final product.

The core technical contributions — the ElevenLabs WebSocket pipeline, the OpenAI-powered summarisation and memory injection system, the Oculus LipSync avatar animation, and the multi-environment VR experience — each represent non-trivial engineering achievements that collectively push beyond the state of publicly available XR social skills tools.

The project's primary limitations — dependency on cloud APIs, hardware resource constraints on standalone VR headsets, and English-only support — represent well-understood engineering trade-offs rather than fundamental barriers. These limitations chart a clear path for future development: offline LLM integration, optimised avatar rendering, and multilingual support are natural next steps that the modular architecture of VR-ASSM is designed to accommodate.

In conclusion, VR-ASSM proves the viability of AI-powered, memory-enabled VR mentoring as a new paradigm for accessible mental wellness support. The project contributes both a working software artifact and a replicable architectural template for future XR therapeutic applications.

---

---

# 11. References

Freeman, D., Lambe, S., Kabir, T., Petit, A., Rosebrock, L., Yu, L., Dudley, R., Powling, R., Chapman, K., Hartley, B., Waite, F., Emsley, R., & Manley, J. (2022). Automated psychological therapy using immersive virtual reality for treatment of fear of heights: a single-blind, parallel-group, randomised controlled trial. *The Lancet Psychiatry*, 9(1), 21–31. https://doi.org/10.1016/S2215-0366(21)00422-3

Lindner, P., Miloff, A., Hamilton, W., Reuterskiöld, L., Andersson, G., Powers, M. B., & Carlbring, P. (2017). Creating state of the art, next-generation virtual reality exposure therapies for anxiety disorders using consumer hardware platforms: design considerations and future directions. *Cognitive Behaviour Therapy*, 46(5), 404–420. https://doi.org/10.1080/16506073.2017.1280843

Maples-Keller, J. L., Bunnell, B. E., Kim, S. J., & Rothbaum, B. O. (2017). The use of virtual reality technology in the treatment of anxiety and other psychiatric disorders. *Harvard Review of Psychiatry*, 25(3), 103–113. https://doi.org/10.1097/HRP.0000000000000138

OpenAI. (2024). *GPT-4o mini: Advancing cost-efficient intelligence*. OpenAI Blog. https://openai.com/blog/gpt-4o-mini-advancing-cost-efficient-intelligence

ElevenLabs. (2025). *Conversational AI documentation*. ElevenLabs Developer Portal. https://elevenlabs.io/docs/conversational-ai/overview

Meta. (2024). *Oculus LipSync SDK for Unity*. Meta Developer Documentation. https://developer.oculus.com/documentation/unity/audio-ovrlipsync-unity/

Unity Technologies. (2024). *XR Interaction Toolkit documentation* (Version 2.6.5). Unity Manual. https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@2.6/manual/index.html

Convai Technologies. (2024). *Convai Unity SDK documentation*. Convai Developer Portal. https://docs.convai.com/api-docs/plugins-and-integrations/unity-plugin

Npgsql Contributors. (2024). *Npgsql 7.x documentation: .NET data provider for PostgreSQL*. Npgsql Documentation. https://www.npgsql.org/doc/index.html

World Health Organization. (2022). *World mental health report: Transforming mental health for all*. WHO Press. https://www.who.int/publications/i/item/9789240049338

Botella, C., Fernández-Álvarez, J., Guillén, V., García-Palacios, A., & Baños, R. (2017). Recent progress in virtual reality exposure therapy for phobias: A systematic review. *Current Psychiatry Reports*, 19(7), 42. https://doi.org/10.1007/s11920-017-0788-4

Riva, G., Baños, R. M., Botella, C., Mantovani, F., & Gaggioli, A. (2016). Transforming experience: The potential of augmented reality and virtual reality for enhancing personal and clinical change. *Frontiers in Psychiatry*, 7, 164. https://doi.org/10.3389/fpsyt.2016.00164
