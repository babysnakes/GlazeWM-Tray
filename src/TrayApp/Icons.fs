module GlazeWM.TrayApp.Icons

open Avalonia.FuncUI.DSL
open Avalonia.Controls
open Avalonia.Media

let checkIcon = "M382-240 154-468l57-57 171 171 367-367 57 57-424 424Z"
let copyIcon =
    "M360-240q-33 0-56.5-23.5T280-320v-480q0-33 23.5-56.5T360-880h360q33 0 56.5 23.5T800-800v480q0 33-23.5 56.5T720-240H360Zm0-80h360v-480H360v480ZM200-80q-33 0-56.5-23.5T120-160v-560h80v560h440v80H200Zm160-240v-480 480Z"
let collapseIcon =
    "m296-80-56-56 240-240 240 240-56 56-184-184L296-80Zm184-504L240-824l56-56 184 184 184-184 56 56-240 240Z"

let pathIcon data =
    PathIcon.create
        [ PathIcon.data (Geometry.Parse data)
          PathIcon.width 16.0
          PathIcon.height 16.0 ]
