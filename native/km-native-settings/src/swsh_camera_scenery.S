// SPDX-License-Identifier: GPL-3.0-only
.section .text,"ax",%progbits
.align 2

// A finite floor from the base Wild Area terrain policy. Only the distance
// operands change, so the original data and native LOD thresholds stay intact.
// All registers except the replaced load's destination and dead LR are retained,
// including NZCV and the full 128 bits of the temporary vector register.
.macro DISTANCE name, dest, base, offset
    .global \name
    .hidden \name
    .type \name,%function
\name:
    ldr \dest, [\base, #\offset]
    stp x16, x17, [sp, #-32]!
    str q31, [sp, #16]
    adrp x16, km_swsh_camera_scenery_active
    add x16, x16, #:lo12:km_swsh_camera_scenery_active
    ldarb w16, [x16]
    cbz w16, 1f
    mov w16, #0xe800
    movk w16, #0x477d, lsl #16
    fmov s31, w16
    fmax \dest, \dest, s31
1:
    ldr q31, [sp, #16]
    ldp x16, x17, [sp], #32
    ret
    .size \name, . - \name
.endm

DISTANCE km_swsh_camera_terrain_distance, s1, x20, 0xac0
DISTANCE km_swsh_camera_model_distance, s1, x8, 0x20
DISTANCE km_swsh_camera_instance_distance, s1, x22, 0x20
DISTANCE km_swsh_camera_instance_density_distance, s2, x22, 0x20
DISTANCE km_swsh_camera_actor_distance, s1, x19, 0x370
