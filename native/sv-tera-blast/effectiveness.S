// SPDX-License-Identifier: GPL-3.0-only
.text
.global tera_effectiveness, normal_effectiveness, tera_effectiveness_value
tera_effectiveness:
    stp x19, x30, [sp, #-16]!
    mov x19, x0
    ldr x8, [x0, #0x38]
    ldr x8, [x8, #0x78]
    ldr x8, [x8, #0x20]
    cbz x8, owner_ready
    ldr w8, [x8, #12]
owner_ready:
    cmp w8, w1
    b.ne done
    ldr x8, [x19, #8]
    ldr x0, [x8, #0x30]
    bl lookup_pokemon
    cbz x0, done
    ldrb w8, [x0, #0x714]
    cbz w8, done
    ldrb w8, [x0, #0x715]
    cmp w8, #99
    b.ne done
    ldr x8, [x19, #0x38]
    ldr x8, [x8, #0x78]
    ldr x8, [x8, #0x28]
    mov w1, #0
    cbz x8, target_ready
    ldr w1, [x8, #12]
target_ready:
    ldr x8, [x19, #8]
    ldr x0, [x8, #0x30]
    bl lookup_pokemon
    cbz x0, done
    ldrb w10, [x0, #0x714]
normal_effectiveness:
    mov w11, #7
tera_effectiveness_value:
    mov w12, #8
    cmp w10, #0
    csel w10, w11, w12, eq
    ldr x8, [x19, #0x38]
    ldr x8, [x8, #0x78]
    ldr x8, [x8, #0x1f0]
    cbz x8, done
    ldrb w9, [x8, #10]
    cmp w9, #1
    b.hi done
    str w10, [x8, #12]
    lsl w9, w9, #1
    strb w9, [x8, #10]
done:
    ldp x19, x30, [sp], #16
    ret
.org 192
