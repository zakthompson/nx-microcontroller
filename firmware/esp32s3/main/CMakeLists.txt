# Main component CMakeLists.txt

idf_component_register(
    SRCS
        "main.c"
        "usb_descriptors.c"
        "../../shared/switch_responses.c"
        "../../shared/emulated_spi.c"
        "../../shared/macro_player.c"
    INCLUDE_DIRS
        "."
        "../../shared"
    REQUIRES
        esp_tinyusb
        esp_timer
)

# Add embedded_macro.h to include path if it exists
if(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/embedded_macro.h")
    target_compile_options(${COMPONENT_LIB} PRIVATE -include "${CMAKE_CURRENT_SOURCE_DIR}/embedded_macro.h")
endif()
