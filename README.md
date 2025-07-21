# VR-ASSM Conversational Avatar AI

## Overview
VR-ASSM is a Unity project that brings together real-time speech recognition, conversational AI, voice cloning, and avatar lip sync. The system listens to the user, understands their speech, generates a response, and speaks back using a lifelike avatar.

## Features
- **Speech-to-Text (STT):** Uses Deepgram API for accurate voice transcription.
- **Conversational AI:** Integrates Groq LLM (OpenAI-compatible) for natural dialogue.
- **Text-to-Speech (TTS):** Uses Cartesia API for voice cloning and speech synthesis.
- **Lip Sync:** Realistic mouth movement using Oculus LipSync and blend shapes.
- **Modular Pipeline:** Each component can be tested and improved independently.

## Setup Instructions
1. **Clone the repository:**  
   `git clone https://github.com/yourusername/VR-ASSM.git`
2. **Open in Unity:**  
   Use Unity 2022 or newer for best compatibility.
3. **Insert your API keys:**  
   - Deepgram (STT): In `STTHandler.cs` (via Inspector)
   - Groq (LLM): In `LLMHandler.cs` (via Inspector)
   - Cartesia (TTS): In `CartesiaTTSHandler.cs` (via Inspector)
4. **Assign avatar and blend shapes:**  
   - Set up your avatar in the Unity scene.
   - Assign blend shape indices in `OculusLipSyncBlendShape`.
5. **Build and run:**  
   Test in Play mode or build for your target platform.

## Architecture
```
Microphone Input
      ↓
STTHandler (Deepgram)
      ↓
LLMHandler (Groq/OpenAI)
      ↓
CartesiaTTSHandler (Cartesia TTS)
      ↓
OculusLipSyncBlendShape + Animator
      ↓
Avatar speaks and animates
```

## Challenges & Solutions
- **Voice Activity Detection (VAD):**  
  Tuned silence threshold and duration, added warm-up time, and debug logs for reliable detection.
- **TTS Speed & Clarity:**  
  Adjusted Cartesia parameters and tested voice IDs for optimal quality.
- **Animator Transitions:**  
  Controlled animation parameters via script for instant, smooth transitions.
- **LLM Output Formatting:**  
  Prepended strict language rules and sanitized output for TTS compatibility.
- **Oculus LipSync Integration:**  
  Ensured correct viseme mapping and avatar compatibility for realistic mouth movement.

## License
MIT (or specify your preferred license)

## Credits
- Deepgram (STT)
- Groq (LLM)
- Cartesia (TTS)
- Oculus LipSync
- Unity Technologies

---