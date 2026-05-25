# Node Type Template

Use this template to define one React Flow node type.

## 1. Basic Identity

- `type_name`:
- `display_name`:
- `category`:
- `icon`:
- `description`:
- `role`:
- `priority`:

## 2. Layout

- `default_width`:
- `default_height`:
- `min_width`:
- `min_height`:
- `max_width`:
- `max_height`:
- `resizable`:
- `collapsed_default`:
- `compact_mode`:
- `header_height`:
- `body_padding`:

## 3. Ports

### Port Rules

- `port_mode`: `fixed | dynamic`
- `allow_multi_input`:
- `allow_multi_output`:
- `allow_self_connection`:
- `allow_cycle`:
- `max_incoming_connections`:
- `max_outgoing_connections`:
- `connection_validation`:

### Port List

| id | label | direction | dataType | slot | visible | required | dynamic | notes |
|---|---|---|---|---:|---|---|---|---|
|  |  | input/output | any/image/text/path/number/code |  | yes/no | yes/no | yes/no |  |

## 4. Data Model

- `meta_schema`:
- `required_fields`:
- `optional_fields`:
- `computed_fields`:
- `default_values`:
- `serialization_rules`:
- `normalization_rules`:

## 5. Behavior

- `editable_fields`:
- `inline_edit_supported`:
- `drag_behavior`:
- `resize_behavior`:
- `selection_behavior`:
- `multi_select_behavior`:
- `context_menu_actions`:
- `keyboard_shortcuts`:
- `undo_redo_behavior`:

## 6. Validation

- `field_validation`:
- `port_validation`:
- `connection_validation`:
- `runtime_validation`:
- `error_states`:
- `warning_states`:

## 7. Styling

- `theme_variant`:
- `background`:
- `border_color`:
- `border_radius`:
- `shadow`:
- `title_color`:
- `body_color`:
- `muted_color`:
- `accent_color`:
- `selected_style`:
- `disabled_style`:
- `port_style`:
- `handle_style`:
- `preview_style`:

## 8. Node Content

- `header_fields`:
- `summary_fields`:
- `body_fields`:
- `preview_fields`:
- `custom_controls`:
- `empty_state`:

## 9. Serialization

- `snapshot_key`:
- `migration_version`:
- `backward_compatibility`:
- `import_behavior`:
- `export_behavior`:

## 10. Example Meta

```json
{
  "title": "",
  "summary": "",
  "body": "",
  "collapsed": false,
  "ports": [
    {
      "id": "in",
      "label": "Input",
      "direction": "input",
      "dataType": "any",
      "slot": 0
    },
    {
      "id": "out",
      "label": "Output",
      "direction": "output",
      "dataType": "any",
      "slot": 0
    }
  ]
}
```

## 11. Notes

- `special_rules`:
- `examples_of_usage`:
- `things_not_allowed`:
- `implementation_notes`:

