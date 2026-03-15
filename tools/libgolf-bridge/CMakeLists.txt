cmake_minimum_required(VERSION 3.16)
project(libgolf-bridge CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# ── Locate the pre-built libgolf static library ────────────────────────────
# build.sh clones and builds libgolf into external/libgolf/build/ before
# running this CMakeLists.  We link against the resulting static library
# rather than using add_subdirectory so that libgolf's test/gtest fetch
# does not run during the bridge build.

set(LIBGOLF_ROOT "${CMAKE_SOURCE_DIR}/external/libgolf")
set(LIBGOLF_INCLUDE "${LIBGOLF_ROOT}/include")

find_library(LIBGOLF_LIB
    NAMES golf
    PATHS
        "${LIBGOLF_ROOT}/build"
        "${LIBGOLF_ROOT}/build/lib"
        "${LIBGOLF_ROOT}/build/src"
    NO_DEFAULT_PATH
    REQUIRED
)

message(STATUS "libgolf library: ${LIBGOLF_LIB}")

add_executable(libgolf-bridge bridge.cpp)

target_include_directories(libgolf-bridge PRIVATE "${LIBGOLF_INCLUDE}")
target_link_libraries(libgolf-bridge PRIVATE "${LIBGOLF_LIB}")

# Optimise the release binary; the bridge is called once per shot comparison.
target_compile_options(libgolf-bridge PRIVATE
    $<$<CONFIG:Release>:-O2>
    $<$<CXX_COMPILER_ID:GNU,Clang>:-Wall -Wextra>
)
