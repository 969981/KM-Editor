// SPDX-License-Identifier: GPL-3.0-only
// Fixed address callbacks. The managed patcher substitutes only labeled settings.
.text
.global tera_behavior, category_policy, category_value, type_policy, preview_effectiveness
.global preview_normal, preview_tera, set_category, done
tera_behavior:
    stp x19, x20, [sp, #-48]!
    stp x21, x30, [sp, #32]
    mov x19, x0
    ldr x8, [x0, #0x38]
    ldr x8, [x8, #0x78]
    ldr x8, [x8, #0x18]
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
    mov x20, x0
category_policy:
    ldr x8, [x20]
    ldr x8, [x8, #0x170]
    blr x8
    and w21, w0, #0xffff
    mov x0, x20
    ldr x8, [x20]
    ldr x8, [x8, #0x180]
    blr x8
    cmp w21, w0, uxth
    b.ls type_policy
set_category:
    ldr x8, [x19, #0x38]
    ldr x8, [x8, #0x78]
    ldr x8, [x8, #0xe0]
    cbz x8, type_policy
    ldrb w9, [x8, #10]
    cmp w9, #1
    b.hi type_policy
category_value:
    mov w10, #1
    str w10, [x8, #12]
    lsl w9, w9, #1
    strb w9, [x8, #10]
type_policy:
    ldrb w10, [x20, #0x715]
    cmp w10, #19
    b.eq notify_type
    cmp w10, #99
    mov w9, #18
    csel w10, w9, w10, eq
    ldr x8, [x19, #0x38]
    ldr x8, [x8, #0x78]
    ldr x8, [x8, #0xb8]
    cbz x8, notify_type
    ldrb w9, [x8, #10]
    cmp w9, #1
    b.hi notify_type
    str w10, [x8, #12]
    lsl w9, w9, #1
    strb w9, [x8, #10]
notify_type:
    ldrb w8, [x20, #0x715]
    strb w8, [sp, #16]
    add x1, sp, #16
    mov x0, x19
    bl notify_tera_type
done:
    ldp x21, x30, [sp, #32]
    ldp x19, x20, [sp], #48
    ret
.balign 4
preview_effectiveness:
    // Called after the existing Tera Starstorm dispatch, with the move ID in w8.
    cmp w8, #851
    b.eq tera_preview
    b regular_preview
tera_preview:
    ldr x8, [x23]
    mov x0, x23
    ldr x8, [x8, #0x928]
    blr x8
    mov w1, w19
    bl lookup_preview_pokemon
    mov w8, #0
    cbz x0, preview_result
    ldrb w8, [x0, #0x714]
preview_result:
preview_normal:
    mov w0, #1
preview_tera:
    mov w9, #2
    cmp w8, #0
    csel w0, w0, w9, eq
    b preview_return
.org 320
