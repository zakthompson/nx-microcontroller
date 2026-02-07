cmake_minimum_required(VERSION 3.13)

# Set board (can be overridden with -DPICO_BOARD=pico2_w etc.)
if(NOT DEFINED PICO_BOARD)
    set(PICO_BOARD pico)
endif()

# Include Pico SDK
include($ENV{PICO_SDK_PATH}/external/pico_sdk_import.cmake)

project(nx_standalone C CXX ASM)
set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)

# Initialize the Pico SDK
pico_sdk_init()

add_executable(nx_standalone
    main.c
    usb_descriptors.c
    ../shared/switch_responses.c
    ../shared/emulated_spi.c
    ../shared/macro_player.c
)

target_include_directories(nx_standalone PRIVATE
    ${CMAKE_CURRENT_LIST_DIR}
    ${CMAKE_CURRENT_LIST_DIR}/../shared
)

target_link_libraries(nx_standalone PRIVATE
    pico_stdlib
    hardware_gpio
    hardware_irq
    tinyusb_device
    tinyusb_board
)

# Enable embedded macro if header exists
if(EXISTS ${CMAKE_CURRENT_LIST_DIR}/embedded_macro.h)
    target_compile_definitions(nx_standalone PRIVATE EMBEDDED_MACRO_ENABLED=1)
endif()

# Create map/bin/hex/uf2 outputs
pico_add_extra_outputs(nx_standalone)

# Disable stdio on both USB and UART (we use raw USB HID)
pico_enable_stdio_usb(nx_standalone 0)
pico_enable_stdio_uart(nx_standalone 0)
