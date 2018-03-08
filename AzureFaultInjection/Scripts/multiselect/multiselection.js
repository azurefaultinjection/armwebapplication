$(document).ready(function() {
  $('#example-selected-parents').multiselect({
    buttonContainer: '<div id="example-selected-parents-container"></div>',
    onChange: function(options, selected) {
      // Get checkbox corresponding to option:
      var value = $(options).val();
      var $input = $('#example-selected-parents-container input[value="' + value + '"]');

      // Adapt label class:
      if (selected) {
        $input.closest('label').addClass('active');
      } else {
        $input.closest('label').removeClass('active');
      }
    }
  });
});
//$(document).ready(function () {
//  $('#example-collapse').multiselect();
//});