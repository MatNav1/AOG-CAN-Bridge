#include <winsock2.h>
#include <windows.h>

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

#ifndef AOG_CAN_BRIDGE_PORT
#define AOG_CAN_BRIDGE_PORT 19000
#endif

namespace
{
	constexpr std::uint32_t Magic = 0x43474F41;
	constexpr int PacketSize = 30;
	constexpr std::uint16_t BridgePort = AOG_CAN_BRIDGE_PORT;
	constexpr std::uint16_t PcanUsbBus1 = 0x51;
	constexpr std::uint32_t ErrorOk = 0;
	constexpr std::uint32_t ErrorQueueEmpty = 0x20;

	struct CanMessage
	{
		std::uint32_t id;
		std::uint8_t type;
		std::uint8_t length;
		std::uint8_t data[8];
	};

	struct CanTimestamp
	{
		std::uint32_t millis;
		std::uint16_t overflow;
		std::uint16_t micros;
	};

	using Initialize = std::uint32_t(__stdcall *)(std::uint16_t, std::uint16_t, std::uint32_t, std::uint32_t, std::uint16_t);
	using Uninitialize = std::uint32_t(__stdcall *)(std::uint16_t);
	using Read = std::uint32_t(__stdcall *)(std::uint16_t, CanMessage *, CanTimestamp *);
	using Write = std::uint32_t(__stdcall *)(std::uint16_t, CanMessage *);

	bool same_endpoint(const sockaddr_in &left, const sockaddr_in &right)
	{
		return left.sin_addr.s_addr == right.sin_addr.s_addr && left.sin_port == right.sin_port;
	}

	void mock_broker(std::atomic_bool &ready, std::atomic_bool &stop)
	{
		WSADATA winsock{};
		WSAStartup(MAKEWORD(2, 2), &winsock);
		SOCKET socketHandle = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
		sockaddr_in local{};
		local.sin_family = AF_INET;
		local.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
		local.sin_port = htons(BridgePort);
		if (bind(socketHandle, reinterpret_cast<sockaddr *>(&local), sizeof(local)) == SOCKET_ERROR)
		{
			ready = true;
			stop = true;
			return;
		}
		DWORD timeout = 50;
		setsockopt(socketHandle, SOL_SOCKET, SO_RCVTIMEO, reinterpret_cast<const char *>(&timeout), sizeof(timeout));
		std::vector<sockaddr_in> clients;
		ready = true;
		while (!stop)
		{
			std::uint8_t packet[PacketSize]{};
			sockaddr_in sender{};
			int senderSize = sizeof(sender);
			const int count = recvfrom(socketHandle, reinterpret_cast<char *>(packet), PacketSize, 0,
			                           reinterpret_cast<sockaddr *>(&sender), &senderSize);
			if (count != PacketSize) continue;
			std::uint32_t magic = 0;
			std::memcpy(&magic, packet, sizeof(magic));
			if (magic != Magic || packet[4] != 1) continue;
			bool known = false;
			for (const auto &client : clients) known |= same_endpoint(client, sender);
			if (!known) clients.push_back(sender);
			if (packet[5] == 1)
			{
				packet[5] = 4;
				sendto(socketHandle, reinterpret_cast<const char *>(packet), PacketSize, 0,
				       reinterpret_cast<sockaddr *>(&sender), sizeof(sender));
			}
			else if (packet[5] == 2)
			{
				packet[5] = 3;
				for (const auto &client : clients)
					if (!same_endpoint(client, sender))
						sendto(socketHandle, reinterpret_cast<const char *>(packet), PacketSize, 0,
						       reinterpret_cast<const sockaddr *>(&client), sizeof(client));
			}
		}
		closesocket(socketHandle);
		WSACleanup();
	}

	bool wait_for_frame(Read read, std::uint32_t expectedId)
	{
		const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
		while (std::chrono::steady_clock::now() < deadline)
		{
			CanMessage message{};
			CanTimestamp timestamp{};
			const auto result = read(PcanUsbBus1, &message, &timestamp);
			if (result == ErrorOk)
				return message.id == expectedId && message.length == 3 && message.data[0] == 0xAA;
			if (result != ErrorQueueEmpty) return false;
			Sleep(2);
		}
		return false;
	}
}

int main(int argc, char **argv)
{
	if (argc != 2)
	{
		std::cerr << "Usage: PcanBasicBridgeClientTest <proxy-dll>" << std::endl;
		return 2;
	}
	std::atomic_bool ready{ false };
	std::atomic_bool stop{ false };
	std::thread broker(mock_broker, std::ref(ready), std::ref(stop));
	while (!ready) Sleep(1);
	if (stop)
	{
		broker.join();
		std::cerr << "Port " << BridgePort << " is already in use." << std::endl;
		return 3;
	}

	const std::string firstPath = std::string(argv[1]) + ".test-a.dll";
	const std::string secondPath = std::string(argv[1]) + ".test-b.dll";
	CopyFileA(argv[1], firstPath.c_str(), FALSE);
	CopyFileA(argv[1], secondPath.c_str(), FALSE);
	HMODULE first = LoadLibraryA(firstPath.c_str());
	HMODULE second = LoadLibraryA(secondPath.c_str());
	auto initializeA = reinterpret_cast<Initialize>(GetProcAddress(first, "CAN_Initialize"));
	auto initializeB = reinterpret_cast<Initialize>(GetProcAddress(second, "CAN_Initialize"));
	auto uninitializeA = reinterpret_cast<Uninitialize>(GetProcAddress(first, "CAN_Uninitialize"));
	auto uninitializeB = reinterpret_cast<Uninitialize>(GetProcAddress(second, "CAN_Uninitialize"));
	auto readA = reinterpret_cast<Read>(GetProcAddress(first, "CAN_Read"));
	auto readB = reinterpret_cast<Read>(GetProcAddress(second, "CAN_Read"));
	auto writeA = reinterpret_cast<Write>(GetProcAddress(first, "CAN_Write"));
	auto writeB = reinterpret_cast<Write>(GetProcAddress(second, "CAN_Write"));
	bool passed = first && second && initializeA && initializeB && uninitializeA && uninitializeB &&
	              readA && readB && writeA && writeB;
	passed &= passed && initializeA(PcanUsbBus1, 0x011C, 0, 0, 0) == ErrorOk;
	passed &= passed && initializeB(PcanUsbBus1, 0x011C, 0, 0, 0) == ErrorOk;
	CanMessage fromA{ 0x18FF1234, 0x02, 3, { 0xAA, 0x01, 0x02 } };
	CanMessage fromB{ 0x18FF5678, 0x02, 3, { 0xAA, 0x03, 0x04 } };
	passed &= passed && writeA(PcanUsbBus1, &fromA) == ErrorOk && wait_for_frame(readB, fromA.id);
	passed &= passed && writeB(PcanUsbBus1, &fromB) == ErrorOk && wait_for_frame(readA, fromB.id);
	if (uninitializeA) uninitializeA(PcanUsbBus1);
	if (uninitializeB) uninitializeB(PcanUsbBus1);
	if (first) FreeLibrary(first);
	if (second) FreeLibrary(second);
	DeleteFileA(firstPath.c_str());
	DeleteFileA(secondPath.c_str());
	stop = true;
	broker.join();
	std::cout << (passed ? "PASS" : "FAIL") << std::endl;
	return passed ? 0 : 1;
}
