// SPDX-License-Identifier: GPL-3.0-only
.section .text,"ax",%progbits
.align 2
.global km_swsh_battle_items_original
.hidden km_swsh_battle_items_original
.type km_swsh_battle_items_original,%function
km_swsh_battle_items_original:
    sub sp, sp, #0x40
    stp x22, x21, [sp, #0x10]
    stp x20, x19, [sp, #0x20]
    stp x29, x30, [sp, #0x30]
    adrp x17, km_swsh_battle_items_continue
    ldr x17, [x17, #:lo12:km_swsh_battle_items_continue]
    br x17
.size km_swsh_battle_items_original, . - km_swsh_battle_items_original
