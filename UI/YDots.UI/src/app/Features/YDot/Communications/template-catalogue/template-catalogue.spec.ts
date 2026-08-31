import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TemplateCatalogue } from './template-catalogue';

describe('TemplateCatalogue', () => {
  let component: TemplateCatalogue;
  let fixture: ComponentFixture<TemplateCatalogue>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TemplateCatalogue],
    }).compileComponents();

    fixture = TestBed.createComponent(TemplateCatalogue);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
