# VR-ASSM Conversational Avatar AI - Comprehensive Requirements Document

## 1. PROJECT OVERVIEW

### 1.1 Project Name
VR-ASSM (Virtual Reality - Automated Speech & Synthesis Module) Conversational Avatar AI

### 1.2 Project Description
A Unity-based conversational AI system that creates an interactive virtual avatar capable of real-time speech recognition, natural language processing, text-to-speech synthesis, and realistic lip synchronization. The system provides a complete conversational pipeline from voice input to animated avatar response.

### 1.3 Project Goals
- Create an immersive conversational experience with lifelike avatars
- Demonstrate integration of multiple AI services in Unity
- Provide a modular, extensible framework for conversational AI applications
- Enable real-time, natural dialogue interactions

### 1.4 Target Platforms
- Unity Editor (Development/Testing)
- Windows Standalone
- VR Platforms (Oculus/Meta Quest compatible)

## 2. FUNCTIONAL REQUIREMENTS

### 2.1 Core Features

#### 2.1.1 Speech-to-Text (STT) Processing
- **FR-001**: System shall capture audio input from microphone in real-time
- **FR-002**: System shall implement Voice Activity Detection (VAD) to automatically detect speech start/end
- **FR-003**: System shall transcribe audio to text using Deepgram API
- **FR-004**: System shall handle audio recording sessions up to 60 seconds
- **FR-005**: System shall support 16kHz audio sampling rate for optimal API compatibility

#### 2.1.2 Natural Language Processing
- **FR-006**: System shall process user text input through Groq LLM API
- **FR-007**: System shall maintain conversational context and personality
- **FR-008**: System shall generate contextually appropriate responses
- **FR-009**: System shall filter output to remove special characters incompatible with TTS
- **FR-010**: System shall implement conversation safety guidelines and content filtering

#### 2.1.3 Text-to-Speech (TTS) Synthesis
- **FR-011**: System shall convert LLM responses to natural-sounding speech using Cartesia API
- **FR-012**: System shall support voice cloning capabilities
- **FR-013**: System shall generate audio in real-time with minimal latency
- **FR-014**: System shall provide configurable voice parameters (voice ID, model selection)

#### 2.1.4 Avatar Animation and Lip Sync
- **FR-015**: System shall animate avatar lip movements synchronized with TTS audio
- **FR-016**: System shall use Oculus LipSync for viseme generation
- **FR-017**: System shall support blend shape-based facial animation
- **FR-018**: System shall integrate with Unity Animator for full-body animations
- **FR-019**: System shall provide smooth transitions between talking and idle states

#### 2.1.5 Conversation Flow Management
- **FR-020**: System shall initiate conversations with greeting message
- **FR-021**: System shall manage conversation state (listening, processing, responding)
- **FR-022**: System shall provide visual/audio feedback during processing states
- **FR-023**: System shall handle conversation continuity after each interaction cycle

### 2.2 User Interface Requirements
- **FR-024**: System shall provide on-screen status indicators for system state
- **FR-025**: System shall display transcribed text for user verification
- **FR-026**: System shall provide configuration interface for API settings
- **FR-027**: System shall include debug logging for troubleshooting

## 3. TECHNICAL REQUIREMENTS

### 3.1 Software Requirements

#### 3.1.1 Unity Engine
- **TR-001**: Unity Editor version 2022.3.48f1 or newer
- **TR-002**: Unity TextMeshPro package support
- **TR-003**: Unity Animation Rigging package (version 1.2.1+)
- **TR-004**: Unity Input System package (version 1.11.0+)

#### 3.1.2 Third-Party Integrations
- **TR-005**: Oculus LipSync SDK integration for viseme processing
- **TR-006**: Convai SDK integration for additional AI capabilities
- **TR-007**: Newtonsoft.Json package for API communication
- **TR-008**: CC3 Unity Tools for character system support

### 3.2 Hardware Requirements

#### 3.2.1 Minimum System Requirements
- **TR-009**: Windows 10 64-bit or newer
- **TR-010**: 8GB RAM minimum, 16GB recommended
- **TR-011**: DirectX 11 compatible graphics card
- **TR-012**: Microphone input device
- **TR-013**: Audio output device (speakers/headphones)
- **TR-014**: Stable internet connection for API calls

#### 3.2.2 VR Requirements (Optional)
- **TR-015**: Oculus/Meta Quest headset compatibility
- **TR-016**: Hand tracking capability (if implemented)
- **TR-017**: Adequate play space for VR interactions

### 3.3 API Requirements

#### 3.3.1 Deepgram Speech-to-Text API
- **TR-018**: Valid Deepgram API key with sufficient credits
- **TR-019**: HTTPS connectivity to api.deepgram.com
- **TR-020**: Support for streaming audio recognition
- **TR-021**: Model compatibility with English language processing

#### 3.3.2 Groq LLM API
- **TR-022**: Valid Groq API key with model access
- **TR-023**: HTTPS connectivity to api.groq.com
- **TR-024**: Support for llama3-8b-8192 model or equivalent
- **TR-025**: OpenAI-compatible API endpoint format

#### 3.3.3 Cartesia Text-to-Speech API
- **TR-026**: Valid Cartesia API key
- **TR-027**: HTTPS connectivity to api.cartesia.ai
- **TR-028**: Support for Sonic-2 TTS model
- **TR-029**: Voice cloning model access

## 4. SYSTEM ARCHITECTURE

### 4.1 Component Architecture
```
Audio Input (Microphone)
    ↓
STTHandler (Deepgram API)
    ↓
AvatarAIController (Central Coordinator)
    ↓
LLMHandler (Groq API)
    ↓
CartesiaTTSHandler (Cartesia API)
    ↓
OculusLipSyncBlendShape + Avatar Animation
    ↓
Audio/Visual Output
```

### 4.2 Data Flow
1. **Input Phase**: Microphone captures user speech
2. **Recognition Phase**: STTHandler processes audio → text transcript  
3. **Processing Phase**: LLMHandler generates AI response
4. **Synthesis Phase**: CartesiaTTSHandler converts text → audio
5. **Output Phase**: Avatar speaks with lip sync animation

### 4.3 Core Components

#### 4.3.1 AvatarAIController.cs
- **Central state management and coordination**
- Voice Activity Detection (VAD)
- Pipeline orchestration
- Animation state control

#### 4.3.2 STTHandler.cs  
- Audio recording management
- Deepgram API integration
- Audio format conversion (WAV)
- Transcription processing

#### 4.3.3 LLMHandler.cs
- Groq API communication  
- Conversation context management
- Response formatting and filtering
- Content safety processing

#### 4.3.4 CartesiaTTSHandler.cs
- Text-to-speech synthesis
- Audio clip generation
- Voice parameter management
- Real-time audio processing

#### 4.3.5 OculusLipSyncBlendShape.cs
- Viseme-driven lip sync
- Blend shape animation
- Audio synchronization
- Real-time facial animation

## 5. ASSET INVENTORY

### 5.1 Scripts (Assets/Scripts/)
| File | Purpose | Dependencies |
|------|---------|--------------|
| **AvatarAIController.cs** | Main conversation pipeline controller | STTHandler, LLMHandler, CartesiaTTSHandler, OculusLipSyncBlendShape |
| **STTHandler.cs** | Speech-to-text processing | Deepgram API, Unity WebRequest |
| **LLMHandler.cs** | Language model integration | Groq API, Unity WebRequest |
| **CartesiaTTSHandler.cs** | Text-to-speech synthesis | Cartesia API, Unity WebRequest |
| **OculusLipSyncBlendShape.cs** | Lip synchronization controller | Oculus LipSync SDK |

### 5.2 Scenes (Assets/Scenes/)
| File | Purpose |
|------|---------|
| **SampleScene.unity** | Main demonstration scene with configured avatar |

### 5.3 Animation Assets
| File | Purpose |
|------|---------|
| **Talking Controller.controller** | Avatar animator controller for speech states |

### 5.4 Audio Assets
| File | Purpose |
|------|---------|
| **male-spoken-i-can-t-figure-her-out_93bpm.wav** | Sample audio file for testing |

### 5.5 Third-Party SDK Assets

#### 5.5.1 Oculus LipSync (Assets/Oculus/LipSync/)
- **Purpose**: Provides viseme detection and lip sync functionality
- **Key Components**: Scripts, models, prefabs, audio processing
- **License**: Oculus SDK License

#### 5.5.2 Convai SDK (Assets/Convai/)
- **Purpose**: Additional conversational AI capabilities and tools
- **Key Components**: Art assets, custom packages, demos, prefabs, tutorials
- **License**: Convai SDK License

### 5.6 Unity Package Dependencies
| Package | Version | Purpose |
|---------|---------|---------|
| **TextMesh Pro** | 3.0.9 | Advanced text rendering |
| **Input System** | 1.11.0 | Modern input handling |
| **Animation Rigging** | 1.2.1 | Advanced character animation |
| **Newtonsoft Json** | 3.2.1 | JSON serialization for API calls |
| **Burst** | 1.8.17 | High-performance computing |
| **Collections** | 2.2.1 | Optimized data structures |
| **CC Unity Tools** | Latest | Character Creator integration |

## 6. PERFORMANCE REQUIREMENTS

### 6.1 Response Time Requirements
- **PF-001**: Speech recognition latency < 3 seconds for 10-second audio clips
- **PF-002**: LLM response generation < 5 seconds for typical queries  
- **PF-003**: TTS synthesis < 4 seconds for 50-word responses
- **PF-004**: Total conversation turn-around time < 12 seconds
- **PF-005**: Lip sync synchronization accuracy within 100ms of audio

### 6.2 System Performance
- **PF-006**: Maintain 30+ FPS during conversation processing
- **PF-007**: Memory usage < 2GB during normal operation
- **PF-008**: CPU utilization < 80% during peak processing
- **PF-009**: Support concurrent audio recording and playback

### 6.3 Network Performance  
- **PF-010**: API calls shall timeout after 30 seconds
- **PF-011**: System shall handle network interruptions gracefully
- **PF-012**: Implement retry logic for failed API calls
- **PF-013**: Optimize API request payload size

## 7. QUALITY REQUIREMENTS

### 7.1 Reliability
- **QR-001**: System shall handle API failures without crashing
- **QR-002**: Implement comprehensive error logging and recovery
- **QR-003**: Graceful degradation when services are unavailable
- **QR-004**: Validate all API responses before processing

### 7.2 Usability
- **QR-005**: Intuitive conversation initiation (automatic greeting)
- **QR-006**: Clear visual feedback for system states
- **QR-007**: Natural conversation flow with minimal user intervention
- **QR-008**: Comprehensive setup documentation

### 7.3 Maintainability
- **QR-009**: Modular architecture for independent component updates
- **QR-010**: Comprehensive code documentation and commenting
- **QR-011**: Configurable parameters via Unity Inspector
- **QR-012**: Extensible design for additional AI service integration

### 7.4 Security
- **QR-013**: Secure API key management (not hardcoded in builds)
- **QR-014**: HTTPS-only communication with external services
- **QR-015**: Input validation for all user and API data
- **QR-016**: No sensitive data logging in production builds

## 8. CONFIGURATION REQUIREMENTS

### 8.1 API Configuration
Each handler script requires API configuration through Unity Inspector:

#### 8.1.1 STTHandler Configuration
- **Deepgram API Key**: Valid API key for speech recognition
- **Audio Settings**: Sample rate (16kHz), recording duration (60s max)

#### 8.1.2 LLMHandler Configuration  
- **Groq API Key**: Valid API key for language model access
- **Model Selection**: llama3-8b-8192 (configurable)
- **API Endpoint**: https://api.groq.com/openai/v1/chat/completions

#### 8.1.3 CartesiaTTSHandler Configuration
- **Cartesia API Key**: Valid API key for TTS synthesis
- **Voice ID**: Configurable voice selection (default: b8b49b88-c1af-4647-b02d-b18db1b8ded0)
- **Model ID**: sonic-2 (configurable)

#### 8.1.4 Avatar Configuration
- **Skinned Mesh Renderer**: Avatar mesh for blend shape animation
- **Viseme Blend Shape Indices**: Mapping for lip sync animation
- **Animator Controller**: State machine for avatar animations

### 8.2 VAD (Voice Activity Detection) Configuration
- **Silence Threshold**: 0.05 (adjustable sensitivity)
- **Silence Duration**: 2.0 seconds (auto-stop timeout)
- **VAD Warmup Time**: 1.0 seconds (initial calibration period)

## 9. DEPLOYMENT REQUIREMENTS

### 9.1 Development Environment Setup
1. **Unity Installation**: Unity Hub with Unity 2022.3.48f1+
2. **SDK Integration**: Import Oculus LipSync and Convai SDKs
3. **Package Installation**: Install all required Unity packages
4. **API Registration**: Obtain API keys for all services
5. **Scene Configuration**: Set up avatar and assign components

### 9.2 Build Requirements
- **Target Platform**: Configure for intended deployment platform
- **API Key Security**: Use secure key management for production builds  
- **Optimization**: Configure build settings for optimal performance
- **Testing**: Validate all functionality before deployment

### 9.3 Runtime Requirements
- **Network Access**: Ensure connectivity to all required APIs
- **Permissions**: Microphone access permissions
- **Resource Management**: Adequate storage for temporary audio files
- **Service Monitoring**: API service availability and quota monitoring

## 10. FUTURE ENHANCEMENT OPPORTUNITIES

### 10.1 Potential Improvements
- **Multi-language Support**: Extend to additional language models
- **Emotion Recognition**: Facial expression and sentiment analysis
- **Memory System**: Persistent conversation memory across sessions
- **Voice Customization**: Real-time voice parameter adjustment
- **VR Hand Tracking**: Enhanced interaction modalities
- **Multiplayer Support**: Multi-user conversation environments

### 10.2 Scalability Considerations
- **Local AI Models**: Offline processing capabilities  
- **Cloud Deployment**: Scalable server-side processing
- **Mobile Optimization**: Lightweight versions for mobile VR
- **Performance Monitoring**: Real-time analytics and optimization

---

## APPENDICES

### Appendix A: API Documentation Links
- **Deepgram**: https://developers.deepgram.com/
- **Groq**: https://console.groq.com/docs/
- **Cartesia**: https://docs.cartesia.ai/

### Appendix B: Unity Package References
- **Oculus LipSync**: https://developer.oculus.com/downloads/package/oculus-lipsync-unity/
- **Convai**: https://convai.com/

### Appendix C: Troubleshooting Guide
- **Common Issues**: API connectivity, microphone permissions, avatar setup
- **Debug Logging**: Comprehensive logging system for issue diagnosis
- **Performance Optimization**: Guidelines for improving conversation response times

---

**Document Version**: 1.0  
**Last Updated**: March 6, 2026  
**Project Version**: Unity 2022.3.48f1  
**Status**: Active Development