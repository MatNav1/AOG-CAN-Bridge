cmake_minimum_required(VERSION 3.16)
project(PcanBasicBridgeClient LANGUAGES CXX)

set(AOG_CAN_BRIDGE_PORT 19000 CACHE STRING "UDP loopback port used by the bridge protocol")

add_library(PCANBasic SHARED
  pcanbasic_bridge.cpp
  pcanbasic_bridge.def
  version.rc)
target_compile_features(PCANBasic PRIVATE cxx_std_17)
target_compile_definitions(PCANBasic PRIVATE
  WIN32_LEAN_AND_MEAN
  NOMINMAX
  AOG_CAN_BRIDGE_PORT=${AOG_CAN_BRIDGE_PORT})
target_link_libraries(PCANBasic PRIVATE ws2_32)
set_target_properties(PCANBasic PROPERTIES
  OUTPUT_NAME "PCANBasic"
  PREFIX ""
  WINDOWS_EXPORT_ALL_SYMBOLS OFF)

add_executable(PcanBasicBridgeClientTest bridge_proxy_test.cpp)
target_compile_features(PcanBasicBridgeClientTest PRIVATE cxx_std_17)
target_compile_definitions(PcanBasicBridgeClientTest PRIVATE
  WIN32_LEAN_AND_MEAN
  NOMINMAX
  AOG_CAN_BRIDGE_PORT=${AOG_CAN_BRIDGE_PORT})
target_link_libraries(PcanBasicBridgeClientTest PRIVATE ws2_32)
