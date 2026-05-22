# VR-ASSM Conversational Avatar AI

## Overview
VR-ASSM is a Unity VR project (Meta Quest) that combines real-time voice conversation, avatar lip sync, and user personalization. The headset talks to **ElevenLabs ConvAI** for speech-in / speech-out AI dialogue, and to an **intermediate HTTPS server** (Node.js on Railway) for sign-in, user profiles, and conversation history stored in **PostgreSQL (Aiven)**.

## Features
- **Voice conversation:** ElevenLabs ConvAI WebSocket agent (STT + LLM + TTS in one pipeline).
- **Lip sync:** Oculus LipSync blend shapes driven by agent TTS audio.
- **User auth:** Sign-up / sign-in through the intermediate server (not direct DB access from Quest).
- **Personalization:** Past conversation summaries loaded from PostgreSQL and injected into the agent at call start.
- **VR locomotion:** Thumbstick movement, snap turn, office + nature environments.
- **Conversation export:** Transcripts saved back to PostgreSQL when a call ends.

## Architecture

```
Meta Quest (Unity)
      │
      ├──────────────────────────────────────┐
      │                                      │
      ▼                                      ▼
ElevenLabs ConvAI                    Intermediate Server
(wss://api.elevenlabs.io)            (Express on Railway)
      │                                      │
      │  mic PCM ↑                           │  HTTPS + X-API-Key
      │  agent audio ↓                       ▼
      │                               PostgreSQL (Aiven)
      │                               users, conversation_history
      ▼
Avatar lip sync + animator
```

**Why the intermediate server?** Quest builds cannot safely connect to PostgreSQL directly (credentials, TLS, firewall). The Node server holds `DATABASE_URL` and `API_KEY` on Railway, exposes a small REST API, and Unity calls that API over HTTPS.

## Intermediate Server (Auth API)

Location: `backend/`

Stack: **Node.js 18+**, **Express**, **pg** (PostgreSQL), deployed on **Railway** with health check at `/health`.

### Environment variables

Copy `backend/.env.example` to `backend/.env` locally:

| Variable | Purpose |
|----------|---------|
| `DATABASE_URL` | Aiven PostgreSQL connection string |
| `PORT` | Server port (Railway sets this in production) |
| `API_KEY` | Shared secret; Unity sends it as header `X-API-Key` |

### API endpoints

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/health` | Liveness + database connectivity check |
| `POST` | `/auth/sign-in` | Verify `user_id` exists and is active |
| `POST` | `/auth/sign-up` | Create user (`full_name`, `age`, `intro_text`) |
| `POST` | `/auth/last-login` | Update `last_login_at` timestamp |
| `GET` | `/users/:userId/context?limit=N` | Profile + recent conversation summaries for agent prompt context |
| `POST` | `/conversation-history` | Insert or update a conversation transcript |

All routes except `/` and `/health` require a valid `X-API-Key` header when `API_KEY` is set.

### Run locally

```bash
cd backend
cp .env.example .env   # fill in DATABASE_URL and API_KEY
npm install
npm start
```

Server listens on `http://localhost:8080` by default.

### Deploy to Railway

1. Create a Railway service pointing at the `backend/` folder.
2. Set `DATABASE_URL` and `API_KEY` in Railway environment variables.
3. Deploy; note the public HTTPS URL (e.g. `https://your-app.up.railway.app`).
4. Confirm `/health` returns `{ "ok": true, "database": true }`.

`backend/railway.toml` configures Nixpacks build, `npm start`, and health check path.

## Unity ↔ Server configuration

Quest builds read API settings from **`Assets/Resources/AuthApiRuntimeConfig.asset`** (bundled in the APK):

| Field | Purpose |
|-------|---------|
| `baseUrl` | Railway HTTPS URL of the intermediate server |
| `apiKey` | Same value as Railway `API_KEY` |
| `useAuthApi` | Enable server-backed auth instead of local-only mode |
| `overrideInspectorOnDeviceBuilds` | Use this asset on device builds (recommended for Quest) |

Unity scripts involved:

- `AuthApiClient.cs` — HTTP client for all `/auth/*` and `/users/*` calls
- `UIManager.cs` — sign-in / sign-up UI; loads runtime config on boot
- `AvatarAIController.cs` — fetches user context at conversation start; gates calls on auth
- `ConversationHistoryExporter.cs` — POSTs transcripts to `/conversation-history`

ElevenLabs keys (`elevenLabsApiKey`, `elevenLabsAgentId`, etc.) remain on `AvatarAIController` in the Unity Inspector.

## Setup Instructions

1. **Clone the repository**
2. **Deploy the intermediate server** (see above) and create PostgreSQL tables (`users`, `conversation_history`)
3. **Configure Unity**
   - Open in Unity 2022.3+ (Quest Android build target)
   - Set `AuthApiRuntimeConfig.baseUrl` and `apiKey` to match Railway
   - Set ElevenLabs agent/voice IDs on `AvatarAIController`
   - Assign avatar blend shapes in `OculusLipSyncBlendShape`
4. **Build for Quest** and test sign-in → start call → walk in office/nature

## VR Office Conversation Setup

1. Add an XR rig root object to the scene (Oculus/XR Origin).
2. Add a `CharacterController` to the rig root.
3. Add `VRLocomotionController` to the same rig root.
4. Assign references in `VRLocomotionController`:
   - `Head Transform`: VR camera transform
   - `Seat Anchor`: where the user stands in the office
   - `Avatar Look Target`: avatar head/chest transform
5. Add `VRConversationControls` to an active scene object.
6. In `VRConversationControls`, assign:
   - `Avatar AI Controller`
   - `Locomotion Controller`
7. Play in VR:
   - Left thumbstick: move
   - Right thumbstick: snap turn
   - Primary button (`A` / `X`): Start / End Call

## Desktop Fallback Controls (No Headset)

1. Select the player root (XR Origin or camera rig).
2. Ensure it has a `CharacterController`.
3. Add `DesktopArrowKeyMovement` and assign `View Transform` to the active camera.

Controls: arrow keys to move and rotate (desktop testing only).

## Avatar Companion Follow (Optional)

1. Add `AvatarCompanionFollow` to the avatar root.
2. Leave `Player Target` empty — it auto-binds to the XR Origin camera on Quest.
3. Assign `Lip Sync Blend Shape` and `Avatar Animator`.

Suggested values: min 1.5 m, max 2.0 m, preferred 1.7 m from player.

## Challenges & Solutions

- **Quest cannot reach PostgreSQL directly:** Intermediate Express server on Railway with HTTPS + API key.
- **Device auth URL drift:** `AuthApiRuntimeConfig.asset` in `Resources/` overrides Inspector values on Quest builds.
- **Speaker bleed / phantom STT:** Mic bleed suppression and transcript filtering in `AvatarAIController`.
- **VR height / locomotion:** Calibrated ground follow via `FixedVrOriginWorldHeight` + `VRLocomotionController`.
- **Oculus LipSync:** Viseme mapping on avatar blend shapes for realistic mouth movement.

## License
MIT (or specify your preferred license)

## Credits
- ElevenLabs (ConvAI voice agent)
- Aiven (PostgreSQL)
- Railway (intermediate server hosting)
- Oculus LipSync
- Unity Technologies

---