#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>

#include <chrono>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <cwctype>
#include <mutex>
#include <string>

namespace
{
	using TPCANHandle = std::uint16_t;
	using TPCANBaudrate = std::uint16_t;
	using TPCANStatus = std::uint32_t;

	struct TPCANMsg
	{
		std::uint32_t ID;
		std::uint8_t MSGTYPE;
		std::uint8_t LEN;
		std::uint8_t DATA[8];
	};

	struct TPCANTimestamp
	{
		std::uint32_t millis;
		std::uint16_t millis_overflow;
		std::uint16_t micros;
	};

	constexpr TPCANStatus PCAN_ERROR_OK = 0;
	constexpr TPCANStatus PCAN_ERROR_QRCVEMPTY = 0x20;
	constexpr TPCANStatus PCAN_ERROR_ILLHW = 0x1400;
	constexpr TPCANStatus PCAN_ERROR_ILLPARAMTYPE = 0x4000;
	constexpr TPCANStatus PCAN_ERROR_ILLMODE = 0x80000;
	constexpr TPCANStatus PCAN_ERROR_INITIALIZE = 0x4000000;
	constexpr std::uint8_t PCAN_MESSAGE_EXTENDED = 0x02;
	constexpr TPCANHandle PCAN_USBBUS1 = 0x51;
	constexpr TPCANHandle PCAN_USBBUS8 = 0x58;

	constexpr std::uint32_t Magic = 0x43474F41;
	constexpr std::uint8_t ProtocolVersion = 1;
	constexpr std::uint8_t Hello = 1;
	constexpr std::uint8_t FrameFromClient = 2;
	constexpr std::uint8_t FrameToClient = 3;
	constexpr std::uint8_t Heartbeat = 4;
	constexpr int PacketSize = 30;
	constexpr std::uint16_t BridgePort = 19000;

	std::mutex stateMutex;
	SOCKET bridgeSocket = INVALID_SOCKET;
	TPCANHandle initializedChannel = 0;
	std::uint16_t clientId = 0;
	std::uint64_t lastHelloMs = 0;
	bool bridgeMode = false;
	HMODULE directModule = nullptr;
	using InitializeFunction = TPCANStatus(__stdcall *)(TPCANHandle, TPCANBaudrate, std::uint32_t, std::uint32_t, std::uint16_t);
	using UninitializeFunction = TPCANStatus(__stdcall *)(TPCANHandle);
	using ReadFunction = TPCANStatus(__stdcall *)(TPCANHandle, TPCANMsg *, TPCANTimestamp *);
	using WriteFunction = TPCANStatus(__stdcall *)(TPCANHandle, TPCANMsg *);
	using GetErrorTextFunction = TPCANStatus(__stdcall *)(TPCANStatus, std::uint16_t, char *);
	InitializeFunction directInitialize = nullptr;
	UninitializeFunction directUninitialize = nullptr;
	ReadFunction directRead = nullptr;
	WriteFunction directWrite = nullptr;
	GetErrorTextFunction directGetErrorText = nullptr;

	std::uint16_t identify_client(TPCANHandle channel)
	{
		wchar_t path[MAX_PATH]{};
		GetModuleFileNameW(nullptr, path, MAX_PATH);
		std::wstring executable(path);
		std::transform(executable.begin(), executable.end(), executable.begin(),
		               [](wchar_t character) { return static_cast<wchar_t>(std::towlower(character)); });
		if (executable.find(L"agisovirtualterminal") != std::wstring::npos) return 2;
		if (executable.find(L"aog-taskcontroller") != std::wstring::npos) return 3;
		return static_cast<std::uint16_t>(10 + channel - PCAN_USBBUS1);
	}

	std::uint64_t now_ms()
	{
		return static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::milliseconds>(
		  std::chrono::steady_clock::now().time_since_epoch()).count());
	}

	bool send_packet(std::uint8_t type, const TPCANMsg *message)
	{
		if (bridgeSocket == INVALID_SOCKET) return false;
		std::uint8_t packet[PacketSize]{};
		std::memcpy(packet, &Magic, 4);
		packet[4] = ProtocolVersion;
		packet[5] = type;
		std::memcpy(packet + 6, &clientId, 2);
		if (message)
		{
			std::memcpy(packet + 8, &message->ID, 4);
			const auto timestampUs = static_cast<std::uint64_t>(now_ms()) * 1000ULL;
			std::memcpy(packet + 12, &timestampUs, 8);
			packet[20] = message->LEN;
			packet[21] = (message->MSGTYPE & PCAN_MESSAGE_EXTENDED) != 0 ? 1 : 0;
			std::memcpy(packet + 22, message->DATA, 8);
		}

		sockaddr_in destination{};
		destination.sin_family = AF_INET;
		destination.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
		destination.sin_port = htons(BridgePort);
		return sendto(bridgeSocket, reinterpret_cast<const char *>(packet), PacketSize, 0,
		              reinterpret_cast<sockaddr *>(&destination), sizeof(destination)) == PacketSize;
	}

	void send_hello_if_due()
	{
		const auto current = now_ms();
		if (current - lastHelloMs >= 500)
		{
			send_packet(Hello, nullptr);
			lastHelloMs = current;
		}
	}

	void close_socket()
	{
		if (bridgeSocket != INVALID_SOCKET)
		{
			closesocket(bridgeSocket);
			bridgeSocket = INVALID_SOCKET;
			WSACleanup();
		}
		initializedChannel = 0;
		clientId = 0;
		lastHelloMs = 0;
		bridgeMode = false;
	}

	bool load_direct_library()
	{
		directModule = LoadLibraryW(L"PCANBasicDirect.dll");
		if (!directModule) return false;
		directInitialize = reinterpret_cast<InitializeFunction>(GetProcAddress(directModule, "CAN_Initialize"));
		directUninitialize = reinterpret_cast<UninitializeFunction>(GetProcAddress(directModule, "CAN_Uninitialize"));
		directRead = reinterpret_cast<ReadFunction>(GetProcAddress(directModule, "CAN_Read"));
		directWrite = reinterpret_cast<WriteFunction>(GetProcAddress(directModule, "CAN_Write"));
		directGetErrorText = reinterpret_cast<GetErrorTextFunction>(GetProcAddress(directModule, "CAN_GetErrorText"));
		if (directInitialize && directUninitialize && directRead && directWrite && directGetErrorText) return true;
		FreeLibrary(directModule);
		directModule = nullptr;
		return false;
	}
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_Initialize(
  TPCANHandle channel, TPCANBaudrate baudrate, std::uint32_t hardwareType,
  std::uint32_t ioPort, std::uint16_t interrupt)
{
	std::lock_guard<std::mutex> lock(stateMutex);
	if (channel < PCAN_USBBUS1 || channel > PCAN_USBBUS8) return PCAN_ERROR_ILLHW;
	if (bridgeSocket != INVALID_SOCKET)
		return channel == initializedChannel ? PCAN_ERROR_OK : PCAN_ERROR_ILLHW;

	WSADATA data{};
	if (WSAStartup(MAKEWORD(2, 2), &data) != 0) return PCAN_ERROR_INITIALIZE;
	bridgeSocket = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
	if (bridgeSocket == INVALID_SOCKET)
	{
		WSACleanup();
		return PCAN_ERROR_INITIALIZE;
	}
	u_long nonBlocking = 1;
	ioctlsocket(bridgeSocket, FIONBIO, &nonBlocking);
	int receiveBufferSize = 4 * 1024 * 1024;
	setsockopt(bridgeSocket, SOL_SOCKET, SO_RCVBUF,
	           reinterpret_cast<const char *>(&receiveBufferSize), sizeof(receiveBufferSize));
	sockaddr_in local{};
	local.sin_family = AF_INET;
	local.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
	local.sin_port = 0;
	if (bind(bridgeSocket, reinterpret_cast<sockaddr *>(&local), sizeof(local)) == SOCKET_ERROR)
	{
		close_socket();
		return PCAN_ERROR_INITIALIZE;
	}

	initializedChannel = channel;
	// Keep VT and TC source/binaries untouched. The proxy identifies the calling
	// executable, so both applications may continue to select PCAN-USB 1.
	clientId = identify_client(channel);
	send_packet(Hello, nullptr);
	lastHelloMs = now_ms();

	// The bridge acknowledges Hello. If it is stopped, transparently fall back
	// to the original PEAK DLL so a single VT or TC still works directly.
	const auto deadline = now_ms() + 200;
	while (now_ms() < deadline)
	{
		std::uint8_t response[PacketSize]{};
		sockaddr_in sender{};
		int senderSize = sizeof(sender);
		const int count = recvfrom(bridgeSocket, reinterpret_cast<char *>(response), PacketSize, 0,
		                           reinterpret_cast<sockaddr *>(&sender), &senderSize);
		std::uint32_t magic = 0;
		if (count == PacketSize) std::memcpy(&magic, response, 4);
		if (count == PacketSize && magic == Magic && response[4] == ProtocolVersion &&
		    response[5] == Heartbeat)
		{
			bridgeMode = true;
			return PCAN_ERROR_OK;
		}
		Sleep(5);
	}

	close_socket();
	initializedChannel = channel;
	if (!load_direct_library()) return PCAN_ERROR_INITIALIZE;
	return directInitialize(channel, baudrate, hardwareType, ioPort, interrupt);
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_Uninitialize(TPCANHandle channel)
{
	std::lock_guard<std::mutex> lock(stateMutex);
	if (channel != initializedChannel && channel != 0) return PCAN_ERROR_ILLHW;
	if (directModule)
	{
		TPCANStatus result = directUninitialize(channel);
		FreeLibrary(directModule);
		directModule = nullptr;
		directInitialize = nullptr; directUninitialize = nullptr; directRead = nullptr;
		directWrite = nullptr; directGetErrorText = nullptr;
		initializedChannel = 0;
		return result;
	}
	if (bridgeSocket != INVALID_SOCKET) close_socket();
	return PCAN_ERROR_OK;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_Read(
  TPCANHandle channel, TPCANMsg *message, TPCANTimestamp *timestamp)
{
	std::lock_guard<std::mutex> lock(stateMutex);
	if (directModule) return directRead(channel, message, timestamp);
	if (!bridgeMode || bridgeSocket == INVALID_SOCKET || channel != initializedChannel) return PCAN_ERROR_INITIALIZE;
	send_hello_if_due();
	std::uint8_t packet[PacketSize]{};
	bool frameReceived = false;
	// Heartbeat replies share the socket with CAN data. Skip them in the same
	// call instead of reporting an empty CAN queue while data may already be
	// waiting behind the heartbeat.
	for (int packetIndex = 0; packetIndex < 64; ++packetIndex)
	{
		sockaddr_in sender{};
		int senderSize = sizeof(sender);
		const int count = recvfrom(bridgeSocket, reinterpret_cast<char *>(packet), PacketSize, 0,
		                           reinterpret_cast<sockaddr *>(&sender), &senderSize);
		if (count != PacketSize) return PCAN_ERROR_QRCVEMPTY;
		std::uint32_t magic = 0;
		std::memcpy(&magic, packet, 4);
		if (magic == Magic && packet[4] == ProtocolVersion && packet[5] == FrameToClient && packet[20] <= 8)
		{
			frameReceived = true;
			break;
		}
	}
	if (!frameReceived) return PCAN_ERROR_QRCVEMPTY;

	if (message)
	{
		std::memcpy(&message->ID, packet + 8, 4);
		message->MSGTYPE = (packet[21] & 1) != 0 ? PCAN_MESSAGE_EXTENDED : 0;
		message->LEN = packet[20];
		std::memcpy(message->DATA, packet + 22, 8);
	}
	if (timestamp)
	{
		std::uint64_t timestampUs = 0;
		std::memcpy(&timestampUs, packet + 12, 8);
		const std::uint64_t millis = timestampUs / 1000ULL;
		timestamp->millis = static_cast<std::uint32_t>(millis);
		timestamp->millis_overflow = static_cast<std::uint16_t>(millis >> 32);
		timestamp->micros = static_cast<std::uint16_t>(timestampUs % 1000ULL);
	}
	return PCAN_ERROR_OK;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_Write(
  TPCANHandle channel, TPCANMsg *message)
{
	std::lock_guard<std::mutex> lock(stateMutex);
	if (directModule) return directWrite(channel, message);
	if (!bridgeMode || bridgeSocket == INVALID_SOCKET || channel != initializedChannel) return PCAN_ERROR_INITIALIZE;
	if (!message || message->LEN > 8) return PCAN_ERROR_ILLHW;
	send_hello_if_due();
	return send_packet(FrameFromClient, message) ? PCAN_ERROR_OK : PCAN_ERROR_INITIALIZE;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_GetErrorText(
  TPCANStatus error, std::uint16_t, char *buffer)
{
	if (!buffer) return PCAN_ERROR_ILLHW;
	if (directModule && directGetErrorText) return directGetErrorText(error, 0, buffer);
	const char *text = "AOG CAN Bridge error";
	if (error == PCAN_ERROR_OK) text = "No error";
	else if (error == PCAN_ERROR_QRCVEMPTY) text = "Receive queue empty";
	else if (error == PCAN_ERROR_INITIALIZE) text = "AOG CAN Bridge is not initialized";
	else if (error == PCAN_ERROR_ILLHW) text = "Unsupported virtual PCAN channel";
	std::strncpy(buffer, text, 255);
	buffer[255] = '\0';
	return PCAN_ERROR_OK;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_Reset(TPCANHandle channel)
{
	std::lock_guard<std::mutex> lock(stateMutex);
	if (directModule)
	{
		using Function = TPCANStatus(__stdcall *)(TPCANHandle);
		auto function = reinterpret_cast<Function>(GetProcAddress(directModule, "CAN_Reset"));
		return function ? function(channel) : PCAN_ERROR_ILLPARAMTYPE;
	}
	return (bridgeMode && channel == initializedChannel) ? PCAN_ERROR_OK : PCAN_ERROR_INITIALIZE;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_GetStatus(TPCANHandle channel)
{
	std::lock_guard<std::mutex> lock(stateMutex);
	if (directModule)
	{
		using Function = TPCANStatus(__stdcall *)(TPCANHandle);
		auto function = reinterpret_cast<Function>(GetProcAddress(directModule, "CAN_GetStatus"));
		return function ? function(channel) : PCAN_ERROR_ILLPARAMTYPE;
	}
	return (bridgeMode && channel == initializedChannel) ? PCAN_ERROR_OK : PCAN_ERROR_INITIALIZE;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_FilterMessages(
  TPCANHandle channel, std::uint32_t, std::uint32_t, std::uint8_t)
{
	std::lock_guard<std::mutex> lock(stateMutex);
	return (directModule || (bridgeMode && channel == initializedChannel)) ? PCAN_ERROR_OK : PCAN_ERROR_INITIALIZE;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_GetValue(
  TPCANHandle channel, std::uint8_t parameter, void *buffer, std::uint32_t bufferLength)
{
	std::lock_guard<std::mutex> lock(stateMutex);
	if (directModule)
	{
		using Function = TPCANStatus(__stdcall *)(TPCANHandle, std::uint8_t, void *, std::uint32_t);
		auto function = reinterpret_cast<Function>(GetProcAddress(directModule, "CAN_GetValue"));
		return function ? function(channel, parameter, buffer, bufferLength) : PCAN_ERROR_ILLPARAMTYPE;
	}
	if (!bridgeMode || channel != initializedChannel) return PCAN_ERROR_INITIALIZE;
	return PCAN_ERROR_ILLPARAMTYPE;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_SetValue(
  TPCANHandle channel, std::uint8_t parameter, void *buffer, std::uint32_t bufferLength)
{
	std::lock_guard<std::mutex> lock(stateMutex);
	if (directModule)
	{
		using Function = TPCANStatus(__stdcall *)(TPCANHandle, std::uint8_t, void *, std::uint32_t);
		auto function = reinterpret_cast<Function>(GetProcAddress(directModule, "CAN_SetValue"));
		return function ? function(channel, parameter, buffer, bufferLength) : PCAN_ERROR_ILLPARAMTYPE;
	}
	// PCAN-Basic permits several configuration values before Initialize. They do
	// not affect the broker transport, so accepting them keeps newer clients ABI-compatible.
	if (channel >= PCAN_USBBUS1 && channel <= PCAN_USBBUS8 && buffer && bufferLength > 0)
		return PCAN_ERROR_OK;
	return PCAN_ERROR_ILLPARAMTYPE;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_InitializeFD(TPCANHandle, const char *)
{
	return PCAN_ERROR_ILLMODE;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_ReadFD(TPCANHandle, void *, std::uint64_t *)
{
	return PCAN_ERROR_ILLMODE;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_WriteFD(TPCANHandle, void *)
{
	return PCAN_ERROR_ILLMODE;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_LookUpChannel(char *, TPCANHandle *foundChannel)
{
	if (!foundChannel) return PCAN_ERROR_ILLHW;
	*foundChannel = PCAN_USBBUS1;
	return PCAN_ERROR_OK;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_InitializeXL(TPCANHandle, const char *)
{
	return PCAN_ERROR_ILLMODE;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_ReadXL(TPCANHandle, void *, std::uint64_t *)
{
	return PCAN_ERROR_ILLMODE;
}

extern "C" __declspec(dllexport) TPCANStatus __stdcall CAN_WriteXL(TPCANHandle, void *)
{
	return PCAN_ERROR_ILLMODE;
}

BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID)
{
	return TRUE;
}
