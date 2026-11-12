# FC27 knockout tie venues

`match_stadium` can be set on a kind-5 Compdata group. For a knockout stage,
each verified two-place group represents a tie. For example, groups 398 and
399 under semi-final stage 397 can carry separate `match_stadium` values 155
and 156. This mapping was supplied by the user. A two-leg tie shares that
group, so the editor explicitly applies its venue to both legs.

Phase settings carry typed tie ordinals and optional original native group
IDs. The compiler uses its generated group list to resolve ordinals; existing
competitions use their preserved native group list. The adapter checks group
ownership and two-place capacity and writes only the group's settings row.
Changing a phase venue preserves its group exceptions. Inheritance removes
only the chosen group's local override.

The calendar stages copies of the same venue view models used in phase
essentials. Apply updates the phase draft and calendar history together;
Cancel is read-only. Venue choices follow the tie across dates and both legs.
Native assigned fixtures without a stable planned-tie mapping stay unavailable
for individual editing. League/group fixtures and rotating host pools remain
unsupported. No stadium field is added to fixture files and club stadiums are
not changed.

Unknown stadium IDs, duplicate native venue rows and non-two-place groups
are preserved and disabled in the guided editor. FC26 keeps its native values
until its conversion is validated. Tests cover native serialization,
inheritance, preservation, history, export/reopening and calendar/phase UI;
they do not certify the stadium used in FC27 itself.
