limited-charges-charges-remaining = {$charges ->
    [one] It has [color=fuchsia]{$charges}[/color] charge remaining.
    *[other] It has [color=fuchsia]{$charges}[/color] charges remaining.
}

limited-charges-max-charges = It's at [color=green]maximum[/color] charges.
limited-charges-recharging = {$seconds ->
    [one] There is [color=yellow]{$seconds}[/color] second left until the next charge.
    *[other] There are [color=yellow]{$seconds}[/color] seconds left until the next charge.
}

limited-charges-ammo-component-on-examine = {$charges ->
    [one] It holds [color=fuchsia]{$charges}[/color] charge.
    *[other] It holds [color=fuchsia]{$charges}[/color] charges.
}
limited-charges-ammo-component-after-interact-full = It's already at maximum charges.
limited-charges-ammo-component-after-interact-refilled = You recharge it.
